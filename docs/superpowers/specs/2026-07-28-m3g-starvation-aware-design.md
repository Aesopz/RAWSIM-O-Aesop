# M3G Starvation-Aware Objective：把站台飢餓顯式納入 MILP 目標式

2026-07-28 · branch `setlevel-redesign` · 作者：Aesop + AI 助理

## 1. 問題與動機（5-seed 實證）

貪婪啟發式 HGS-M3 在**相同 T=1 派車上限**下，吞吐與 pile-on 皆勝 M3G（MILP）。5-seed 診斷定位根因：

| o100 (5-seed avg) | station 飢餓 | TP/h |
|---|---|---|
| **M3G (MILP)** | **973s**（seed4 尖峰 1635s） | 287.1 |
| **HGS-M3 (greedy)** | **181s**（穩定 160-218） | 295.8 |

M3G 飢餓是 HGS 的 **5.4×**，每 seed 皆然；HGS TP 每 seed 皆勝。根因：**M3G 的目標式（min 距離 − 完成 + pod-count shortfall）沒有顯式盯站台飢餓**——當某站快餓、但下一台 pod 邊際完成不高時，M3G 覺得「省一趟距離」比「餵站台」划算 → 選擇不派 → 站台餓 → 掉吞吐。既有 `PipelineFloorWeight=20` 的 pod-count shortfall 是個**單位錯（數 pod 非數秒）、力度不足**的代理。

**目標**：把站台飢餓以**秒為單位、決策相依**顯式加入 M3G 目標式，讓 MILP（精確求解）把飢餓壓到 ≈ HGS 水準（~181s），從而**奪回吞吐上限，展現精確求解相對貪婪的優勢**。權重先給合理初值，數值之後再掃。

## 2. 建模：pod-selection 目標式的飢餓懲罰

**複用現成、經驗證的基礎設施**（`StarveAwareCost` 已用於 `SAM1GManager`/`SAHADGSManager`；工作投影 `SlowStartController.ComputeStationWorkProjection`）：

對每個候選派遣變數 `xps[pod, station]`，precompute 該派遣的**飢餓減量（relief）**：
```
baseGap        = ComputeStationWorkProjection(station, now).StarvationGapSec
arrivalAbs     = now + StarveAwareCost.TravelTime(ExactPodStationCost(pod,station), nominalSpeed)
withPodGap     = ComputeStationWorkProjection(station, now,
                    { new StationWorkJob { ArrivalAbs = arrivalAbs,
                                           BaseItems  = <pod 在該站可撿件數>,
                                           Pod = pod, ArrivalConfirmed = false } }).StarvationGapSec
relief(pod,s)  = max(0, baseGap − withPodGap)      // 這台 pod 消掉的飢餓秒數
```
在目標式（`SplitM2eICManager` 的 objective，`xps` 係數）加：
```
xps[pod,station] 係數 +=  − w_starve · relief(pod,station)
```
（目標式為最小化；relief 為正 → 係數變負 → 獎勵「補飢餓站」的派遣。）

**語意**：MILP 現在會為「即將飢餓、且這台 pod 趕得及到」的站台派車以消飢餓——這正是 station-starvation-aware 派遣，用**時間估算（到站時刻）+ 工作 buffer（StarvationGap）**兩者，且**決策相依**（不同 pod/station 的 relief 不同）。

**時間估算範圍**：`ExactPodStationCost` 為自由流距離（<1% 誤差），不碰已放棄的 WHCA* wait-delay 二階修正 → 無 aleatoric 風險。

## 3. 架構（gated，對齊 M3G MINCORE 的 SoftInboundCommitted 前例）

- **新 config 欄位**（`SplitM2eICConfiguration`）：
  - `StarvationAwareDispatch`（bool，預設 `false` = 主開關）
  - `StarvationWeight`（double，w_starve，初值待定；本 spec 建議起手 **1.0**，之後掃）
- **Manager**（`SplitM2eICManager`，gated 加項）：`StarvationAwareDispatch==false` → 整塊跳過，與 M3G MINCORE **逐位一致**（zero-drift）。`==true` → 對每個 `xps` 候選加 relief 項。
- **CLAUDE.md 合規**：`SplitM2eICManager` 為 M3G 的活躍開發檔（本 session MINCORE 的 SoftInboundCommitted/Progress/γ 皆 gated 加於此），故 gated 加飢餓項一致；關掉即現行 M3G。不動 M1G/HADGS/PVGS/HGS 檔。
- **canonical config**：`split_milp_m3g_sa.xconf`（拷貝 `split_milp_m3g.xconf` + 開兩欄位）。M3G MINCORE（`split_milp_m3g.xconf`）保留當對照。

## 4. 驗收（proof-of-mechanism 優先）

1. **zero-drift**：`StarvationAwareDispatch=false` → footprint 與 `split_milp_m3g.xconf` 逐位一致。
2. **飢餓下壓（核心）**：`split_milp_m3g_sa.xconf`（w_starve=初值）o100 Fill inv70 2-seed，`StatStationStarvationTimeSec` **顯著低於現行 M3G 973s，朝 HGS 的 ~181s 逼近**。先證「能壓」，不強求一次到位。
3. **吞吐奪回**：飢餓壓下後，SA-M3G 的 TP **≥ HGS-M3（295.8）**，展現精確求解優勢（若 relief 建模正確，MILP 應能做得比貪婪更好）。
4. **公平**：SA-M3G 與 HGS 共用 T=1 等參數不變，唯一新增是飢餓項。
5. **o200 不死鎖**。
6. **權重掃描**（驗收 2/3 過後）：w_starve ∈ {0.5, 1, 2, 4…} 掃 TP-vs-飢餓 前緣，定 canonical 值。

## 5. 風險與緩解
- **relief 計算成本**：每 epoch 對每個 xps 候選呼叫兩次 `ComputeStationWorkProjection` → 決策時間上升。若過慢，只對「EST 小於門檻（快餓）」的站台算 relief，其餘略過（剪枝，不影響正確性）。
- **relief 恆為 0（項無效）**：若 baseGap 本就 0（現況不餓）則 relief=0、無作用——正常。真正要壓的是「若不派就會餓」的站，其 baseGap>0。驗證時檢查 relief 有非零樣本。
- **過度補站 → 過度派 pod / 能耗升**：w_starve 太大會像 HGS 一樣多派 pod、EOR 略升。掃權重時同時看 EOR，取「飢餓壓到 ~HGS 且 EOR 不明顯惡化」的點。
- **BaseItems 估算**：pod 在站可撿件數取「pod 內容與該站已指派訂單需求的交集」；實作時對照 SplitM2eIC 既有的可撿量計算，勿另發明。

## 6. 驗證計畫
1. Build x64 Release。
2. zero-drift（flag off）。
3. SA-M3G o100 2-seed：StatStationStarvationTimeSec vs M3G 973 / HGS 181；TP vs HGS 295.8。
4. o200 死鎖檢查。
5. relief 非零抽樣確認（log 或斷點）。
6. w_starve 掃描定 canonical。
