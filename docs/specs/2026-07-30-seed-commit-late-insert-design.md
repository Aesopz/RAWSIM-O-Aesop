# 種子承諾 + 到站續單（Seed-Commit / Late-Insert）

2026-07-30 · branch `late-insert`（還原點 `d87c229`）· 作者：Aesop + AI 助理

## 1. 動機

M3G 對貪婪對照 HGS-M3 在小案例 TP 小輸 1.5%、飢餓 346 vs 181。壓飢餓的**參數旋鈕已全部走完且全部失敗**：

| 方向 | 結果 |
|---|---|
| 飢餓寫進目標式（`StarvationWeight`） | 壓不動，capped ~850 |
| 硬性強制補車（`ForceFeedConstraint`） | pile-on 崩、飢餓暴增至 1321→1913 |
| 拿掉軟地板（`PipelineSoftFloorDisabled`） | 更差（842→1045） |
| 調高補車權重（w_pipe 40/60） | 完全 inert，同 seed 逐位相同 |
| 放寬管線深度 T | 飢餓降但 pile-on 崩，退回 M1G |
| 叫車時機 `PipelineFloorLeadSec` 40/70/100 | **70 已是最佳點**，TP 全域變化 <0.5% |

六個方向各自撞到不同的牆，而所有牆背後是同一個原因：**決策在求解瞬間就把訂單、貨架、站台三者綁死，而等貨架真的走到站台時那份快照早已過期**。lead 掃描的不對稱（往早偏 30 秒飢餓 +44%，往晚偏只 +7%，差六倍）是**「資訊過期的代價遠大於車程延遲」**的直接證據。

因此唯一未撞牆的結構性出路 = **把綁定往後推**。

## 2. 設計（brainstorm 定案 2026-07-30）

1. **MILP 一字不改**：目標式、限制式、求解流程全部沿用現行 M3G。單一變因＝**落實範圍**，效益百分之百歸因給綁定時機。模型貢獻（`q[o,i,s]` 定義最優拆單）與既有消融結論完整保留。
2. **只落實種子單**：MILP 解出某站要服務 N 張單時，只真的 `AllocateOrder` 其中**最有價值的一張子單**（種子），其餘 N−1 張的計畫**丟棄**，留待後續重解在更新的狀況下重新決定。種子顆粒度做成設定值可掃描（整張子單／一條 line／一件；起點＝整張子單）。
3. **續單由 MILP 自己做**：不寫任何貪婪插單邏輯。貨架在途／到站後，後續重解時它落在最便宜的成本層（Queued α=1 / OnTheWay β=3），MILP 會自然把更多單分給它。**exact 敘事完整保留**，與使用者在 backfill 議題上「in-MILP，不要外掛啟發式」的立場一致。
4. **全程 gated**：旗標預設關閉 → 與現行 M3G 逐位一致（zero-drift）。
5. **正典 config 不動**：新行為只存在於新的 xconf 變體，既有實驗數據永遠可重現。

## 3. 機械可行性（已逐項讀碼驗證）

| 能力 | 位置 | 狀態 |
|---|---|---|
| MILP 可把單分給已在站貨架 | `SplitM2eICManager` Pb + q 變數、pod 分層 α/β | ✅ 現行 pile-on 的來源，承重中 |
| 配單自動通知引擎有新工作 | `BotManagerPodSelection.cs:1574` `OrderAllocated` → 對所有 bot 設 `_outputStationHasPotentialOnTheFlyWork=true` + 重置 on-the-fly 旗標 | ✅ 自動 |
| 追加工作給執行中的貨架 | `BotManagerPodSelection.cs:1702-1720`；M1G 血緣走 `GetPossibleRequestsofMP` → `extractTask.AddRequest` | ✅ |
| 貨架快做完時叫醒模型續工作（D16） | `OutputStation.cs:258-260 / 302-304`，條件 `PackingBuffer != null && Requests.Count == 1` | ✅ `SplitM2eICManager` 建構子（:40-42）無條件建立 PackingBuffer → **對 M3G 已啟用** |
| 行程合法最低門檻 | `ResourceManager.CreateExtractRequests`（每**件**一個 request） | ✅ 一件即可 |
| 貨架**到站**觸發訊號 | `OrderManager` 只有 SignalOrderFinished / NewOrderAvailable / BundleStored / StationActivated | ❌ **不存在**（見 §5 風險 R2） |

**關鍵約束（決定 SplitM1GLB 失敗的原因，本設計必須遵守）**：`GetPossibleRequestsofMP`（`BotManagerPodSelection.cs:187-210`）以**已配單訂單的 request** 為配對來源，且無條件消耗 `_Ziops` 分錄；呼叫端 `:1170-1171` 在配對結果為空時 `return false`，**行程直接取消**。→ **種子必須在 bot 認領之前就完成 `AllocateOrder`**，不可延後。這是「種子」存在的唯一理由。

## 4. 實作範圍

**唯一改動點＝落實階段**（`SplitM2eICManager.CommitSplitExactResult`，:1856-1867）。現行邏輯對 `result.Allocations` 全部 `AllocateOrder`；新增 gated 分支改為只落實種子，其餘捨棄。

需連帶處理：
- `result.NewZiops`：只保留種子對應的分錄（未落實的訂單不可鎖庫存，否則庫存被計畫佔住卻永不使用）。
- `result.SplitParents`：只有種子真的產生 child 時才走 EPR / `IsFullyClaimed` 流程；被丟棄的計畫不得動到母單狀態。
- `_pendingOrders`：被丟棄的訂單必須留在 backlog，供下次重解使用。

**新設定值**（`SplitM2eICConfiguration`）：
- `SeedCommitOnly`（bool，預設 `false`）＝主開關。
- `SeedGranularity`（enum：`WholeChild` / `SingleLine` / `SingleUnit`，預設 `WholeChild`）。
- `SeedSelectionRule`（enum：`MostUnits` / `MostLinesClosed`，預設 `MostLinesClosed`，與現行目標式的 line-closure 語意對齊）。

**新 config 變體**：`split_milp_m3g_seed.xconf`（拷貝正典，只改上述三值 + `<Name>`）。

## 5. 風險

- **R1 槽位空置驅動超額派車**：只落實種子 → 站台槽位留空 → 目標式的 `Σy = Cs − us` 會在下次重解繼續想填滿 → 可能催出額外派車。**緩解**：`icLGcap ≤ T − future` 硬鎖在途 1 台，物理上無法灌爆。**驗收要看 pod 趟次**：若趟次明顯上升，代表 R1 發生，種子顆粒度需放大。
- **R2 續單來得太晚**：最早的供給端喚醒是 D16（貨架只剩最後一筆工作）。若種子偏大，貨架大半趟程無人續單；若種子偏小，D16 很快觸發。**先不加新的到站觸發**（保持單一變因），由驗收數據決定是否需要。
- **R3 計畫churn**：每次重解都丟棄大部分計畫並重算，可能在相鄰決策間反覆改變主意。種子錨定可抑制。**觀察指標**：決策次數、求解時間。
- **R4 母單/consolidation 會計錯亂**：丟棄的計畫若誤動母單狀態（EPR、`IsFullyClaimed`、`TakeAvailableOrder`），拆單會計會壞。**驗收必看** `splitorders.csv` 的整合鏈與 orders handled 是否自洽。

## 6. 驗收

1. **zero-drift**：`SeedCommitOnly=false` 時，o100 seed 0/1 的 `statistics.txt` 與正典 M3G 逐位一致。
2. **主判準**（o100 Fill inv70 4h，2 seeds，對照正典 M3G 的飢餓 283.4 / TP 295.1 / pile-on 4.62 / PO 4.66 / IPO 11.22 / EOR 1.424 / 趟次 253.5）：
   - **成功**＝飢餓下降，且 pile-on / PO / IPO **不掉**、趟次**不增**。
   - **失敗**＝飢餓下降但 pile-on 崩或趟次暴增（＝退化成 M1G 式的多趟少載，與放寬 T 同義，無新意）。
   - **無效**＝各指標皆在雜訊內 → 綁定時機不是承重因素，收工回報。
3. **拆單會計自洽**：`splitorders.csv` 整合鏈正常、無孤兒 child、orders/items handled 與正典同量級。
4. **無死鎖**：o200 兩 seed 跑滿。
5. 若主判準成功，掃 `SeedGranularity`（WholeChild / SingleLine / SingleUnit）量延後程度的劑量反應。

## 7. 退路

四層，任一層即可回復：
1. 還原點 commit `d87c229`（分支 `setlevel-redesign` 指向它）。
2. 本功能在獨立分支 `late-insert`，失敗即 `git checkout setlevel-redesign`。
3. `SeedCommitOnly=false` → 逐位等同現況。
4. 正典 `split_milp_m3g.xconf` 不動，新行為只在 `split_milp_m3g_seed.xconf`。

## 8. 落地順序

Task 1（三個設定值 + 新 xconf 變體 + zero-drift 驗證）→ Task 2（`CommitSplitExactResult` 的 gated 種子分支，含 NewZiops / SplitParents / _pendingOrders 的連帶處理）→ 驗收 → 視結果掃 `SeedGranularity`。

相關：[[project-m3g]]、`docs/2026-07-27-m3g-coronation.md`、失敗的晚綁定前例 `SplitM1GLBManager`（claim-time hook `OnPodRequestResolution` 全庫零呼叫＝死碼，只剩 watchdog 退化路徑，實測 0 orders handled）。
