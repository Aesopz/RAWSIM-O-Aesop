# Slow-Start：問題定義與當前實作分析

**Date**: 2026-05-29
**Author**: Aesop (with Claude)
**Status**: Problem statement,作為新 EST 設計的前置文件

---

## 1. 我們要解決的問題

### 1.1 RMFS 既有觀察

Output station 是 RMFS 系統的瓶頸。當 bot 載 pod 抵達 station 時，若 queue 已有 pod 排隊，bot 必須在 station 周邊停等。這段「過早抵達」並未帶來吞吐量增益，反而：

1. 在 station 周邊製造交通熱點，引發其他 bot 的 stop-and-go 與多餘 turn
2. 消耗 `P_SUPPORT × 停等時間` 的能量
3. 佔據 station 入口 cell，干擾後續 dispatch

### 1.2 核心假設（slow-start 的理論承諾）

> 把 bot 在 station queue 要排的時間 `W` 改成在 pod cell 主動 hold 的 `D`。
> 若 `D ≤ W` → bot 到站時 queue 剛好排到它 → **throughput 不變、queue wait ↓、path conflict 不增**。

整個論點建在三個假設上：

| 假設 | 內容 | 用到的量 |
|---|---|---|
| **A1** | hold 結束後 bot 實際抵達 station 的時間 ≈ 模型預測的抵達時間 | ETA |
| **A2** | T_starve（station 還能撐多久）的估計不會系統性高估 | EST |
| **A3** | hold 期間 bot 占據 pod cell 對其他 bot 的影響 ≈ 0 | hold-cell impact |

**這三個假設在當前實作裡都以不同程度破掉了** —— 這就是 slow-start 「轉移成功但副作用蓋過利益」的根因。

### 1.3 PP→TA 成本回饋研究脈絡

slow-start 是 PP→TA 成本回饋的具體案例：PP 層的 reservation/ETA 資訊（路徑成本）回饋給 release timing 決策（TA 層的 dispatch 時機）。能否把這個回饋機制做對，是論文核心問題的一個 instance。

---

## 2. 當前實作（2026-05-26 集中式 scheduler 版）

### 2.1 架構演進三代

| 階段 | 機制 | 退役原因 |
|---|---|---|
| **rev1–rev5**（2026-05-20） | 每台 holder 各自 `SlowStartController.ComputeHold` + FIFO senior look-ahead | 對稱 T_starve 導致多台 holder **同步釋放** |
| **靜態 buffer 加大**（2026-05-26） | buffer 10s → 16.8s（多車 est−act 平均離差） | station IdleTime 反升（807→831s）、stop&go +34% —— est−act 離差由壅塞內生，會自我膨脹，靜態緩衝補不掉 |
| **現行：集中式 scheduler**（2026-05-26 後） | `StationReleaseScheduler.Schedule` per-station per-tick，一次選一個 chosen | 仍依賴理想 ETA 下界 + 預測 cascade |

### 2.2 元件邊界

```
PathManager.Update  (每 tick, RAWSimO.Core/Control/PathManager.cs:529-535)
   └── 對每個 OutputStation 呼叫 StationReleaseScheduler.Schedule(station, this, t, buffer)
         ├── 蒐集此站所有 _isSlowStartHolding 的 bot
         ├── starve = SlowStartController.ComputeStationStarvation(station, t)
         │        ← 單伺服器管線,排除「所有」holder
         ├── 每 holder 算 (lift, travel = EstimateIdealKinematicEta, value, proc)
         ├── Decide(starve, holders, buffer)  ← 純函式,可單測
         │     ├── feasible = {h : starve − lift − travel − buffer ≥ 0}
         │     ├── feasible ≠ ∅ → 選 value 最大者(tie-break:早抵達 → bot.ID)
         │     ├── feasible = ∅ → 救火,選 lift+travel 最小者「立即釋放」
         │     └── cascade: 其餘 holder 用 newStarve = (travelFull > starve ? travelFull : starve) + proc
         └── 寫回每台 bot 的 _slowStartReleaseDeadline / _slowStartIsChosen / _slowStartEta / _slowStartTStarve

BotNormal.BotSlowStartHold.Act  (RAWSimO.Core/Bots/BotNormal.cs:2527-2649)
   └── 變成 passive:只讀 _slowStartReleaseDeadline,到時釋放
       第一 tick 仍負責 _isSlowStartHolding=true + ClaimStorageLocation(pod cell)
       釋放時寫回 ExtractTask.ExpectedArrivalAtStation = now + lift + eta
```

### 2.3 關鍵公式

**budget**（per-holder,scheduler 算）：
```
budget_h = starve_time − lift_h − travel_h − buffer
```
- `starve_time`：`ComputeStationStarvation` 回傳，排除所有 holder 的單伺服器管線。
- `lift_h`：`PodTransferTime`（pod 尚未舉 → 2.2s；已舉 → 0）。
- `travel_h`：`EstimateIdealKinematicEta`，**空 reservation table 的 SpaceTimeAStar**（無壅塞下界）。
- `buffer`：`SlowStartEtaSafetyBuffer`，預設 16.8s。

**chosen 規則**：
- `feasible = {h : budget_h ≥ 0}`
- `feasible ≠ ∅` → `chosen = argmax_h value_h`，`deadline = now + budget_chosen`
- `feasible = ∅` → 救火，`chosen = argmin_h (lift+travel)`，立即釋放

**cascade**（其餘 holder 延長 budget）：
```
travel_full = lift_chosen + travel_chosen
newStarve = (travel_full > starve_time) ? travel_full + proc_chosen
                                        : starve_time + proc_chosen
budget'_h = newStarve − lift_h − travel_h − buffer
```

**T_starve 計算（ComputeStationStarvation）**：
1. `stationFreeAt = max(now, station.BlockedUntil)`
2. 對每個 active extract task（含 inbound），算 `arrival`、`baseItems = Requests.Count`
3. 單伺服器管線：按 arrival 排序，若 `arrival > t0` → `return t0`（飢餓缺口）；否則 `t0 += (baseItems + extraItems) × ItemTransferTime`
4. `extraItems = ReservePotentialPicks(pod, openDemand)`，OTF 開時生效

### 2.4 ETA：刻意選擇的理想下界

`EstimateIdealKinematicEta` 用**空 reservation table** 的 SpaceTimeAStar，回的是無衝突下界。

設計理由：實測 reservation-aware probe 在 busy window 失敗率 89%；多車壅塞延誤由 `SlowStartEtaSafetyBuffer` 吸收。

代價：見 §3.1（F1）。

### 2.5 Value vs Proc 刻意分離

| 量 | 內容 | 用途 |
|---|---|---|
| `value_h` | `committed Requests.Count + Σ min(pod.stock, station open demand)` | 選誰先放（argmax） |
| `proc_c` | `chosen.Requests.Count × ItemTransferTime` | cascade 推 `newStarve` |

**注意**：`value_h` 的 open-demand 部分**未 gate** `OnTheFlyExtract`。OTF 關時，open-demand 配對永遠不會實現 → value 含有無法兌現的成分（影響選擇順序，不影響時機）。

### 2.6 設定旗標

```
SlowStartEnabled            = false   (baseline)
SlowStartReleasePolicy      = StationSlack
SlowStartEtaSafetyBuffer    = 0.0  → fallback 16.8s
```

---

## 3. 失效機制：三條假設怎麼破

### F1（最重）：A1 破 —— ETA 是無壅塞下界 + 釋放會自己製造壅塞

**機制**：
1. `EstimateIdealKinematicEta` 是單車無衝突的時間，real travel > ideal travel。
2. hold 把多台 bot 累積成「同一波釋放」 → 釋放後上路互相阻擋 → 壅塞被 hold **內生地放大**。
3. `est−act gap` 不是固定 offset，而是 endogenous bias：**hold 越久 → 釋放越集中 → gap 越大**。
4. 靜態 `buffer = 16.8s` 補不掉這個會自膨脹的偏差。

**閉環**：
```
hold 集中釋放 → 路上瞬時壅塞 → 實際 travel > ideal ETA → 釋放後到站晚於預測
                                                            ↓
T_starve 計入 inbound 用 ideal ETA → EST 高估 → budget 高估 → hold 更久
                                          ↑                       │
                                          └───────────────────────┘
                                  （hold 越久 → 釋放越集中 → ETA gap 越大 → EST 越高估）
```

### F2：A2 破 —— T_starve 系統性高估

**機制**：
- 現行 `PipelineNextFreeTimeWithPotential` 把所有 inbound pod 都按預測抵達排進管線。
- inbound 的 `arrival` 用 ideal ETA → 偏樂觀 → 把「其實晚到」的 pod 誤判成「準時」→ 把那台的工作量也計入 EST。
- OTF 開時還加上「未綁定 demand × pod stock」潛在工時 → 進一步高估。
- 結果：**EST 高估 → budget 高估 → over-hold → station 真的餓**。

**與 F1 互相放大**：F1 讓 ETA 偏樂觀 → F2 的管線把更多 inbound 認定為準時 → EST 更高估 → hold 更久 → F1 的壅塞更嚴重。

### F3：A3 破 —— hold-cell impact 不為零

- bot 在 pod cell 上 hold 期間占據該 cell，且 `ClaimStorageLocation` 鎖住存放點。
- 鄰近路徑的其他 bot 在 reservation table 上會避開它 → 多迂迴或停等。
- 影響相對小於 F1 釋放潮，但非零。

### F4：靜態 buffer 無法追蹤動態壅塞

- `buffer = 16.8s` 是常數，但 est−act gap 的分布是**長尾**。
- 系統閒時 → gap 小 → 16.8s 過保守 → under-hold（省能空間流失，不餓站）。
- 系統忙時 → gap 大 → 16.8s 不夠 → over-hold（餓站）。
- 兩種錯誤一起發生 → 平均看起來「持平」，P95 端故障率上升。

### F5：「一次一個」沒完全消滅同步釋放

- 集中式 scheduler 每 tick 只選一個 chosen，但：
  - chosen 的 `deadline = now + budget` 可能跟「下一 tick 被選中的下一台」的 deadline 很接近。
  - 因為大家都在類似的飢餓窗口、用類似的 ideal travel 算。
- 結果是「**幾秒內連續釋放幾台**」而非真正分散 → 對下游路網的瞬時壓力與同步釋放接近。

---

## 4. 數據證據（2026-05-27 onoff test, small/N=4/28800s）

| KPI | OFF | ON | 變化 | 對應假設 |
|---|---|---|---|---|
| OrdersHandled | 330 | 330 | 持平 ✓ | 承諾守住（test 有 slack） |
| Throughput (orders/hr) | 165 | 165 | 持平 ✓ | 承諾守住 |
| **StationStarvationTime** | 432s | **470s** | **+8.95% ⚠** | **A2 破（F2）** |
| StationStarvationPctOfSimXStations | 6.00% | 6.53% | +0.54pp | A2 破 |
| **PodQueueWait Mean** | 67.6s | **34.0s** | **−49.6% ✓** | 主 hypothesis 成立（轉移成功） |
| PodQueueWait P95 | 165.8s | 108.0s | −34.9% ✓ | 轉移成功 |
| QueueStopAndGoCount | 5 | 2 | −60% ✓ | 站旁壅塞下降 |
| **StopAndGoCount**（一般路徑） | 61 | **79** | **+29.5% ⚠** | **A1 + A3 破（F1, F3）** |
| StopAndGoEnergyKJ | 9.21 | 10.88 | +18.1% ⚠ | F1 |
| **WaitTime** | 231s | **281s** | **+21.6% ⚠** | A1 破（F1） |
| E_SupportKJ | 1141 | 1046 | −8.4% ✓ | hold 省電 |
| E_TotalKJ | 1910 | 1816 | −4.9% ✓ | 總能耗 win（小場景） |
| RobotUtilizationEffective | 0.494 | 0.592 | +20.0% ✓ | 利用率上升 |

**讀法**：queue wait 確實被搬走了（−49.6%），但搬到了路上（StopAndGo +29.5%, WaitTime +21.6%）和 station 飢餓（+8.95%）。throughput 在 small/slack 場景守住，但 large/N=60 飽和區是否守住**未驗證**（設計 spec §5.2 的 Phase 1b 還沒跑）。

---

## 5. 為什麼 throughput 沒掉（在這個 test case）

不是機制保證的結果，是 **slack 給的緩衝**：
- N=4 站、訂單量在系統容量內。
- F1+F2 製造的 starvation 增量（+8.95%）和 stop&go 增量（+29.5%）被 28800s 時間窗吸收。
- 換到 N=45 large 高壓場景，throughput 很可能就守不住。

**這也是為什麼 slow-start「失效」——不是 throughput 掉了，是它「沒兌現承諾的完整版本」**：queue wait 確實 ↓，但同時 path conflict ↑、starvation ↑，淨利只在小場景能維持，飽和區會崩。

---

## 6. 對下一步設計的意義

| 失效機制 | 是否在新 EST 設計範圍 | 處理方式 |
|---|---|---|
| F1（ETA 樂觀 + 自膨脹壅塞） | 部分 | 統一管線會減少同時在途車數 → 間接緩解，但 ETA 本身仍是 ideal |
| **F2（T_starve 高估）** | **主攻** | 新 EST：站 demand pool ∩ pod 內容做即時配對；on-time/late 用 `arrival > t` 自動判 |
| F3（hold-cell impact） | 不在範圍 | 由 `ClaimStorageLocation` 已部分處理；未來 phase 考慮 hold-at-staging |
| F4（靜態 buffer） | 不在範圍 | 短期沿用 16.8s；未來可改成動態 |
| F5（同步釋放殘餘） | 不在範圍 | 短期接受；可加「最小釋放間隔」未來再說 |

**新 EST 設計目標**：把 F2 修掉，讓 starvation 不再因高估而增加；驗證 F2 修掉後 F1/F5 的殘量有多大，再決定要不要動。

詳細設計：`docs/superpowers/specs/2026-05-29-unified-arrival-pipeline-est-design.md`（待寫）。
