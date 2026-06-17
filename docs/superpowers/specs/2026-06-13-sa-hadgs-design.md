# SA-HADGS 設計規格(Starvation-Aware HADGS)

日期:2026-06-13
狀態:已定案(與使用者逐節確認)
相關:`docs/superpowers/plans/2026-06-06-m1g-station-starve-aware.md`(M1G delay-cost 注入)

---

## 1. 背景與問題

### 1.1 實證證據(toobig-45bot、Mu-1000、7200s、seed 0、WHCA*n-P)

| 指標 | HADGS baseline | HADGS+EST站排序(現有 gated 版)|
|---|---|---|
| Type A 飢餓總和 | 20,921.7 s(24.2%)| 21,019.5 s(+0.5%,不變)|
| Pod 交接間隙總和 | 20,754.9 s | 20,832.1 s |
| 完成訂單 | 2,878 | 2,910(+1.1%)|
| 單站 idle 全距 | 175~4,905 s | 1,352~2,874 s(−68% 不均)|
| 壁鐘時間 | ~4 小時 | ~5.5 小時 |

結論:**EST 站排序只重分配飢餓、不降總量**。根因有二:

1. **決策鏈無時間概念**:HADGS 的 pod 評分(`Score()`,HADGSManager.cs:349)與 Gurobi 指派目標式(`SolveByMp`,:711)全是 Manhattan 距離;「選的 pod 趕不趕得上站點斷料(EST)」這個判斷在整條決策鏈中不存在 → pod 普遍晚到(交接間隙 20,754s)。
2. **CPU 熱點**使大規模實驗不可行(7200s 需 ~4 小時):
   - `SimplePOAandPPS` fallback:PiSKU 笛卡兒積枚舉 + 每站每 epoch 最多 30 次 Gurobi 呼叫(Mu-1000 下單一 pod 湊不齊一張單,fallback 幾乎每輪觸發)
   - `Score()` 每評一個候選 pod 就重掃整個 backlog(200+ 張單)的覆蓋

### 1.2 設計需求(使用者定案)

- station EST(時間到斷料)為 POA/PPS/TA 的中心決策依準,目標 station always busy、最大化吞吐
- **演算法執行要快**(解決 4 小時問題)
- EST 為**軟限制**:所有候選都趕不上時,選最小遲到照樣指派(管線永不空轉)
- ETA 用 **名目速度旅行時間**(與 M1G 的 `StarveAwareCost` 同一套,論文口徑一致)
- 迷你 bot↔pod 指派以 **regret 貪婪**取代 Gurobi
- 倉庫沒有 pod 能滿足 EST 時,**POA 彈性換單**(top-K 急單競爭)
- 評分:**delay 懲罰 vs 完成訂單數的加權權衡**(候選評分);TA 配對用字典序(準時優先)

### 1.3 字典序 vs 加權的裁決(已定案)

- **候選組合評分用加權**:第一序鍵(gap 秒數)是連續值、幾乎不平手,字典序會讓完成單數永遠無發言權;供給受限區(Mu-1000)所有候選 gap>0,字典序退化成純 min-gap,犧牲 pile-on → 趟數增加 → 惡性循環。加權是字典序的超集(W_order→0 退化為 gap 至上),sweep W_order 即得 delay↔throughput Pareto 曲線(論文關鍵圖),且與 M1G 的 w1/w2 同構。
- **TA bot↔pod 配對用字典序**:第一序鍵(準時/不準時)是布林、平手常見,字典序(以 BIG 懲罰實作)無需調參。

---

## 2. 目標與非目標

### 目標
1. Type A 飢餓總量下降(非僅重分配)、throughput 上升
2. epoch 決策成本降 1~2 個數量級(7200s Mu-1000 目標 <1 小時壁鐘)
3. baseline HADGS 行為 byte 級不變(新 class,不動共用碼)
4. 與 M1G starve-aware 共用 `StarveAwareCost`,論文三階 ablation:HADGS → HADGS+EST排序 → SA-HADGS

### 非目標(YAGNI)
- 不接 reservation-aware ETA(時空 A* 查詢,與「執行要快」衝突;BAED 補償留為延伸)
- 不做安全水位上限(容量 gate + 堆序已足)
- 不動執行端(BotManager/ExtractTask/`_Ziops1` 介面)
- 不動補貨側

---

## 3. 架構

### 3.1 打包
- 新類別 `SAHADGSManager : OrderManager`(新檔 `RAWSimO.Core/Control/Defaults/OrderBatching/SAHADGSManager.cs`)
- 新設定 `SAHADGSConfiguration : OrderBatchingConfiguration`;`OrderBatchingMethodType` 加 `SAHADGS`;ControllerFactory 加 case;xconf 以 `OBSAHADGS` 選用
- 參數自含於 config(不依賴全域 `StarveAwareCostEnabled`)

### 3.2 沿用 HADGS 的部分
- epoch 觸發(`SituationInvestigated` / δ `OrderBatchingTriggerThreshold`)
- `GenerateOd` 緊急集 + `sequence` 急迫度編號
- inbound pod 同步與 RestTask 清理
- POA 完整覆蓋 gate 與 `_Ziops1` 揀貨請求標記(執行介面)
- KPI snapshot 計數器
- FastLane 尾段邏輯照搬(實驗中 config 維持關閉)

### 3.3 核心量

| 量 | 定義 | 成本 |
|---|---|---|
| `EST_s` | `SlowStartController.ComputeStationWorkProjection(s, now, localJobs).FirstStarveSec`;`localJobs` = 本 epoch 已承諾但尚未生成 ExtractTask 的 (ETA, work) 清單 | O(在途任務數),僅該站被指派後重算 |
| `ETA(b,p,s)` | `(d(b→p) + d(p→s)) / v + 2×PodTransferTime`;d 用現有 `EstimateBotPodDistance`/`EstimatePodStationDistance`(Manhattan/DistanceSet);v = `NominalSpeed`(0 → bot 最大速度) | O(1) |
| `work(p,s)` | pod p 在站 s 被標記的揀貨件數 × `ItemTransferTime` | O(1) |
| `projectedGapSec` | 組合內各 pod (到站時間, work) 按到達序的單伺服器管線模擬(沿用 `PipelineNextFreeTime` 邏輯)得出的總斷檔秒數 | O(p log p),p≤5 |

注意:**止餓看首個 pod 到站,完單看最後一個 pod**;`projectedGapSec` 同時罰首段與中段斷檔。

---

## 4. 演算法

### 4.1 主迴圈:EST 最小堆注水(water-filling)

```
DecideAboutPendingOrders():
  KPI snapshot、同步 inbound(含 RestTask 清理)、GenerateOd/sequence    (沿用)
  Ra = GenerateAvailableBots()
  heap ← {(EST_s, s) | s 可指派}                       // 最小堆,最餓的站在頂
  while heap 非空:
    s = pop 最小 EST
    ① POA pass:現有 inbound 能完整覆蓋的單,按 sequence 急迫度全數指派+標記
       (零邊際成本——不動用新 pod/bot,天然最大化完成單,無需 delay 權衡)
    ② 若站仍有空位、有庫存可行訂單、Ra 非空:
         candidate = 候選評選(§4.2)
         無 candidate → 不回堆,continue
         提交:RegisterInboundPod + ClaimPod + Ra −= bots
         再跑一次 ① 使目標單經同一條 POA 路徑指派(沿用單一指派路徑)
    ③ EST_s 增量重算(localJobs 加入新承諾的 (ETA, work))
       站仍有空位 → 回堆
  終止:堆空;每次 pop 無進展即不回堆 ⇒ 必然終止;另設迭代上限護欄(防禦)
```

「最餓先吃、吃完水位上升、換下一個最餓」= max-min 注水,直接實現 always busy。

### 4.2 候選評選(POA+PPS 聯合,取代 Completeable Score + SimplePOAandPPS)

對站 s、**top-K 張**(K=`TopKOrders`,預設 3)按 `sequence` 最急且庫存可行的訂單——彈性換單:最急的單趕不上時,第 2、3 急的單自動競爭:

1. **每張單建 pod 組合**(取代笛卡兒積):
   - 貪婪 set-cover:反覆選「邊際覆蓋最大」的 pod,平手取 pod→station 旅行時間短者;上限 ≤ |Ra|
   - 變體(`UseEtaGreedyVariant`):按 ETA 升序取 pod 至覆蓋滿
   - 候選 pod 池:`UnusedPods` 中可供應該單任一缺項、未被 Selected/Claim 者
2. **TA bot↔pod 配對(字典序,regret 貪婪)**:
   ```
   score(b,p) = ETA(b,p,s) + (ETA ≤ EST_s + FeasibilitySlackSec ? 0 : BIG)   // BIG = 1e6
   每輪:對每個未配 pod 算 best / 2nd-best bot 分數,
        選 regret = (2nd − best) 最大的 pod 先綁定其 best bot
   ```
   準時到達數最大化 ≻ 時間最小化;O(p²×b) 微秒級,無 solver。
3. **候選組合加權評分(min 為佳)**:
   ```
   score = − OrderRewardSec × completableOrders     // 完成訂單獎勵
           + 1.0            × projectedGapSec       // delay 懲罰(貨幣 = 秒)
           + TravelTimeWeight × Σ travelTimeSec     // 次要:能耗/距離代理
   ```
   - `completableOrders`:組合(+現有 inbound)能完整覆蓋 top-K 急單中的幾張(只評 K 張,不掃全 backlog)
   - W_order 語意:「一張完成單值多少秒斷檔」;W_order=60 時,斷檔 40s 換 2 單划算(−120+40 < −60+0)
4. ≤ 2K 個候選取全域最佳提交。

### 4.3 刪除的熱點(速度來源)

| 原熱點 | 處置 |
|---|---|
| Gurobi 呼叫(≤30 次/站/epoch)| → 0(regret 貪婪)|
| PiSKU 笛卡兒積(指數)| → 貪婪 set-cover(線性)|
| `Score()` 每候選全 backlog 重掃 | → 只評 top-K 張單 |
| 每候選重算站投影 | → EST 每站每次指派後增量重算一次 |

---

## 5. 設定參數(SAHADGSConfiguration)

| 參數 | 預設 | 角色 |
|---|---|---|
| `OrderRewardSec` | 60 | **主 sweep 參數**:delay↔吞吐取捨(對稱 M1G w2)|
| `TravelTimeWeight` | 0.1 | 能耗/距離次要權重 |
| `TopKOrders` | 3 | 彈性換單視野 |
| `UseEtaGreedyVariant` | true | 第二組合變體開關(ablation 點)|
| `FeasibilitySlackSec` | 0 | 準時判定容差(可負值補償 ETA 樂觀偏差)|
| `NominalSpeed` | 0 | ETA 換算速度(0 = bot 最大速度)|

內部常數:`BIG = 1e6`(不開放 config)。

---

## 6. 統計與驗證

### 6.1 新增計數器(進 statistics.txt)
- 可行提交數 vs 遲到提交數、平均遲到秒數
- epoch 壁鐘時間(均值/最大)
- 每 epoch 候選評估數

### 6.2 測試階梯
1. **單元**:regret 配對正確性(小例對照暴力解)、set-cover 覆蓋完整性、管線 gap 模擬與 `PipelineNextFreeTime` 一致、EST 增量單調
2. **冒煙**:small layout 600s 跑通、訂單正常完成、無例外
3. **A/B**(toobig-45bot、Mu-1000、1800s、seed 0,對照已有 baseline:飢餓 3,512.7s / 864 單 / ~70 分壁鐘):
   - Type A 總量↓、throughput↑、壁鐘時間↓(三個都要)
4. **回歸**:baseline HADGS(`OBHADGS`)結果 byte 級不變(未動共用碼的驗證)
5. **Sweep**(論文):W_order ∈ {0, 30, 60, 120, 240} × 2 seeds → delay↔throughput Pareto

### 6.3 風險與對策
| 風險 | 對策 |
|---|---|
| 名目速度 ETA 低估壅塞 → 準時判定偏樂觀 | `FeasibilitySlackSec` 負值補償;延伸:接 BAED(不進 v1)|
| 貪婪 set-cover 非最優組合 | `UseEtaGreedyVariant` 雙變體;K 張單彈性競爭緩解 |
| top-K 視野漏掉更好的遠期單 | K 可調;POA pass 會撿回 inbound 已覆蓋者 |
| 堆迴圈活鎖 | 無進展不回堆 + 迭代上限護欄 |

---

## 7. Ablation 敘事(論文)

1. **HADGS**(距離代理、無時間)→ 飢餓 20.9k s
2. **HADGS + EST 站排序**(順序重分配)→ 不均 −68%,總量持平 —— 證明「順序」不是槓桿
3. **SA-HADGS**(EST 貫穿 POA/PPS/TA、時間化、加權權衡)→ 預期總量下降 + 吞吐上升 + 執行加速
4. **W_order sweep** → delay↔throughput Pareto 前緣
5. 與 **M1G starve-aware**(MILP 路線)對照:同一套 `StarveAwareCost` 口徑
