# M2e-LB（晚綁定 exact）設計

2026-07-12。前情：三個目標家族（legacy / 對齊貪婪 PVGS-E / Coverage-First）全數無法讓 exact 在閉環贏過貪婪；槽位診斷（`docs/2026-07-12-po-gap-diagnosis.md` §9）定量指出真正病灶——**M2e 在求解瞬間就 AllocateOrder 佔槽，訂單駐留 132.9s（含等 pod），槽位佔用 96-102% 飽和，TP=642 貼著 86,400÷132.9≈650 的槽位回收天花板**。PVGS 的隱性正確行為＝晚綁定（駐留 116.7s、佔用 79-92%、天花板 ~740）。**修法＝把「規劃」與「槽綁定」解耦，目標函數一字不動。**

## 0. 設計錨點（brainstorm 定案，2026-07-12）

1. **規劃層目標＝M2e 原目標**，`SolveSplitExact` 的目標與限制式零修改——單一變因，效益百分之百歸因給綁定時機。CF 目標留作後續可疊加旋鈕。
2. **綁定時刻＝pod 實體進入站台佇列區、服務開始前**。駐留預期從 133s 降至 ~117s 以下（佇列＋服務）。
3. **回填做成旗標**（`LateBindingBackfill`，default false）：一期跑純晚綁定（歸因），二期開旗標量第二段增益。
4. **在途上限 W＝可調旋鈕**（`PlannedWipCap`，default 6），掃 {6, 9, 12, 18}。W 取代 MILP 裡 Cs 的語意；W=6＝內建回歸錨。**W 是承重旋鈕**：Little's law 攤開後，若已規劃訂單仍計入 6 槽容量，天花板一分不升；抬天花板的必要條件是規劃在途量可超過實體槽數、實體槽只被「綁定中」訂單佔用。
5. **計畫不可逆**（同現行 Ziops 語意）：規劃即鎖庫存與 pod→站→單三層，重解只用剩餘 W 容量規劃新單。晚資訊紅利由回填旗標承接。
6. **架構＝新鏡像 manager**（`SplitM1GLBManager` ＋ `SplitM1GLBConfiguration : SplitM1GExactConfiguration`），原檔零修改（消融對照組不可動，憲法 1.3）。
7. **驗收＝打最強版 PVGS-M2e（θ=6，TP 648），分階梯**（§6）。

## 1. 機械可行性基礎（已驗證）

- **派 pod 與佔槽在引擎裡本來就是兩條線**：`BotManagerPodSelection.cs:1166-1365` 的 pod 取貨派工只讀 `_Ziops[station]`（solve 時寫入的 Symbol→units 計畫帳本），不讀訂單佔槽狀態。現行 manager 只是在同一瞬間both 做了。
- 現行落地鏈（`SplitM1GExactManager.cs:673-692`）：`NewZiops`→`pod.JustRegisterItem`（鎖庫存）＋`_Ziops[station]` 寫入 → `AllocateOrder`（佔槽、`TimeStampSubmit`）→ Allocator 鏈 `SupplementExtractRequests`（requests 綁 pod）→ 站台註冊。LB 把箭頭後半段整塊搬進 pod 進站事件。
- 晚綁定可行性先行證據：2026-05-31 backfill 探針（memory: project_backfill_probe_validation）——槽位 100% 滿是常態（「保留槽/延遲綁定是必要前提」）、pod 離站時 70%+ 可單獨完整救單（回填旗標的供給面命門）。

### 被淘汰的機械方案（記錄）

- **平行新帳本（不寫 _Ziops）**：PS 層只認 `_Ziops`，不寫它 pod 不會被派；繞過需改 `BotManagerPodSelection`（引擎檔）——違反鏡像原則，淘汰。
- **M2e-SC（逐筆承諾＋事件重解）**：治「批量快照」不治「槽駐留」，與 LB 正交，留作 LB 之後可疊加的二期。
- **PVGS-E2（打殘貪婪）**：作廢——LB 讓 exact 有物理可能正面贏，不需要湊排序。

## 2. 總架構

**新檔案（原檔零修改）**：

| 檔案 | 內容 |
|---|---|
| `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GLBManager.cs` | `SplitM1GExactManager` 忠實鏡像＋LB 執行層。MILP（Initialize/Solve）逐字照抄，**唯二例外**＝Initialize 內 Cs 的計算改 W 口徑（§3）與 pending 集合排除 Ledger 中訂單；另動 `DecideAboutPendingOrders` 提交塊、新增 Binder/Ledger/Watchdog |
| `MethodConfigurationsOB.cs` 追加 | `SplitM1GLBConfiguration : SplitM1GExactConfiguration`（繼承鏈技巧，引擎 `is M1GConfiguration` 檢查自動通過） |
| ControllerFactory | 加一個 case；csproj 手動 `<Compile Include>`（C# 7.3 / net48） |

**組件**（單一職責）：

1. **Planner**＝既有 MILP；唯一改動＝容量輸入 `Cs[s] = max(0, W − inFlight[s])`。
2. **DeferredBindingLedger**＝`Dictionary<Pod, List<LbPendingBinding>>`（manager 私有），`LbPendingBinding = (Order order, OutputStation station, double plannedAt)`。
3. **Binder**＝pod 進站事件處理器：該 pod 名下分錄逐筆 `AllocateOrder` 落地。
4. **Watchdog**＝超時強制綁定（掛 manager 既有 update 週期）。

**新 config 欄位**（全部 default＝行為最保守）：

| 欄位 | 型別 | default | 意義 |
|---|---|---|---|
| `PlannedWipCap` | int | 6 | W：每站「已規劃＋已綁定未完成」在途上限 |
| `BindingWatchdogTimeout` | double | 180 | 秒；規劃後超時未綁定則強制綁定 |
| `LateBindingBackfill` | bool | false | 綁定時刻用 pod 剩餘庫存回填 backlog（二期） |

（XmlSerializer 元素順序陷阱照舊：新欄位加在衍生類尾端、xconf 內按宣告順序排列；行為探針作 safety net。）

## 3. 規劃層（solve 時刻）

**照舊立即發生**：`_Ziops[station]` 寫入（派工引擎）、`pod.JustRegisterItem`（鎖庫存）、split child 建立＋`TransferExtractRequests`（資料層，不佔槽）、parent fully-claimed 移出 pending、parent `TimeStampSubmit` 記錄（統計用，同現行）。

**遞延（搬進 Binder）**：`AllocateOrder`（佔槽＋child/一般單 `TimeStampSubmit`）→ `SupplementExtractRequests` → 站台註冊鏈。solve 時只寫 Ledger 分錄 `(order, station, pod, plannedAt)`。

**規劃即認領（防重複規劃的硬規則）**：solve 時立即把已規劃訂單移出 `_pendingOrders`（現行由 `AllocateOrder` 內的 `_pendingOrders.Remove` 順帶完成，LB 因遞延必須顯式提前做）；fast lane 候選集同步排除 Ledger 中訂單。下一次 solve 的 pending 集合因此天然不含在途規劃，回填旗標掃的 `_pendingOrders` 也不會撞上已規劃單。

**容量語意（W 唯一注入點）**：

```
inFlight[s] = 站上已佔槽未完成（現行 CapacityInUse+CapacityReserved 口徑）
            + Ledger 中屬於 s 的未綁定分錄數
Cs[s] = max(0, PlannedWipCap − inFlight[s])
```

MILP 內部 Cs 用法一行不改。W=6 時語意近似現行 M2e（差異僅在佔槽時點）＝回歸錨。**求解觸發節奏不變**（situation-driven；「有容量的站」判斷改用 W 口徑）。

**FastLane 正交**：fast lane 走實體空槽直配（即到即綁），不經 Ledger、不計入規劃路徑改動。

## 4. 綁定層

**觸發＝三層保險**（由早到晚，功能正確性與掛鉤時點解耦）：

1. **主掛鉤**：pod 抵達站台佇列事件（plan 階段從既有 Instance 通知選定確切鉤點；若無合用事件，退化為 manager update 輪詢「pod 已在站台佇列」——代價一個 tick 延遲）。
2. **懶綁定保底**：揀貨執行路徑索取訂單 requests 前，發現該 pod 有未落地分錄 → 當場綁定。**保證就算主掛鉤漏接，模擬不壞**，只是駐留多幾秒。
3. **Watchdog**：`now − plannedAt > BindingWatchdogTimeout` → 無條件綁定（治 pod 改道/行程異常的尾部）。

**落地順序**（逐筆）：`AllocateOrder(order, station)` → 既有 Allocator 鏈（`SupplementExtractRequests`＋站台註冊）——與現行 M2e 同一段代碼，只是晚 60-120s 執行。分錄移除、寫 `lb_bindings.csv`。

**回填旗標**（default false）：綁定收尾時用該 pod 剩餘未保留庫存掃 `_pendingOrders`，找「單 pod 可完整服務」的訂單（複用 `PvgsExactAligned.PlanSqueeze` 骨架），命中則立即 `JustRegisterItem`＋`_Ziops` 寫入＋`AllocateOrder`（不走遞延）。受 W 與實體容量雙閘門。

**錯誤處理**：
- pod 中途被放回/改道（理論不會——有 `_Ziops` 分錄的是 used pod；防禦性處理）→ watchdog 兜底。
- 綁定時 order 已完成（fast lane 搶先同一張單的殘量情境）→ 分錄作廢跳過，log anomaly。
- 綁定時站台 order pool 滿：允許進站台 queue（現行語意本就允許 allocated order 超過 6 個 active slot），無需特殊處理。

## 5. 觀測性

- `lb_bindings.csv`：`order, pod, station, plannedAt, boundAt, gap, trigger(main/lazy/watchdog)`——「規劃→綁定」間隔分佈＝晚綁定機制的直接論文證據；watchdog/lazy 觸發占比＝掛鉤健康度。
- 駐留（`StatThroughputTime`，submit→complete）自動變成「綁定→完成」，與 M2e 同定義可比——**這是機制主指標**（預期 133→≤117s）。
- 決策 log 沿用 `splitm1gx_decision_log.csv` 格式（欄位不變）。

## 6. 驗收階梯（5 seeds、small 7200s；每級獨立有發表價值）

| 級 | 判準 | 意義 |
|---|---|---|
| 0 | 迴歸：既有 m2e/pvgs xconf 結果 bit-identical（新類型不觸發任何舊路徑） | 安全 |
| 1 | LB(W=6)：駐留顯著下降（→~117s 以下）且 TP ≥ M2e−2% | 機制錨：晚綁定生效且無害 |
| 2 | W 掃描最佳臂：**TP > 650**（突破實測天花板）且 RD/EOR 不劣於 M2e | 天花板抬升＝診斷閉環，核心成果 |
| 3 | LB（+回填若需要）五項（TP/PO/RD/OD/EOR）≥ 原版 PVGS-M2e(648) | 終極目標；未達仍有級 2 可寫 |

xconf：`small/lb_w6.xconf`、`lb_w9`、`lb_w12`、`lb_w18`（backfill 全關）；二期 `lb_w<best>_bf.xconf`。既有檔案照憲法不動；拷貝微調走逐 byte 比對規則（含 CRLF）。

## 7. 風險

- **W 大的老病回歸**：W=18 時未綁定庫存鎖定變多，可能重現早承諾病理——掃描要畫的 U 型曲線本身是論文素材。
- **引擎隱含假設審計**：solve→綁定窗口內，`BotManagerPodSelection` 的 `_Ziops` 消費點（~4 處：1166/1191/1232/1253/1310/1331/1365 帶）是否假設「order 必已佔槽」——**plan 的第一個 task 逐點審計**；審出問題由懶綁定保底兜正確性。
- **站台 KPI 語意位移**：訂單晚進槽，`StatEQueueingAtStation` 等指標口徑改變，報告時註記。
- **瓶頸上游轉移**：槽位鬆綁後瓶頸可能移到 bot 隊（10 bots）——TP 若卡 ~680 而非 740 是 bot-bound 新資訊，非設計失敗。
- **兩段時戳語意**：`TimeStampSubmit` 後移會影響所有以 submit 為基準的既有統計（throughput time、BacklogWeight 評分器讀 `TimeStampQueued`/`TimeStampSubmit`）——LB 臂內自洽、跨臂比較時註記口徑；`TimeStampQueued` 仍在綁定時設（`NewOrderQueuedToStation`），與 submit 同步後移。

## 8. 相關文件

- 槽位診斷（本設計的動機與量化基礎）：`docs/2026-07-12-po-gap-diagnosis.md` §9
- CF 設計（被取代的目標工程路線）：`docs/superpowers/specs/2026-07-12-coverage-first-objective-design.md`
- PVGS-E 驗收失敗報告：`docs/2026-07-12-pvgse-acceptance.md`
- M2e 模型定義：`docs/2026-07-11-m2e-model-definition.md`
- backfill 探針驗證：memory `project_backfill_probe_validation`（2026-05-31）
- 代碼錨點：`SplitM1GExactManager.cs:651-695`（現行提交塊）、`BotManagerPodSelection.cs:1166-1365`（_Ziops 派工消費）、`ResourceManager.cs:520-532`（SupplementExtractRequests）、`ResourceManager.cs:539-554`（NewOrderQueuedToStation）
