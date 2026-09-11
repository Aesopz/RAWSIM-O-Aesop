# M2e-SF 設計 SPEC：Sunk-First Completion-Locked Two-Solve

日期：2026-07-14  
狀態：**已實作；Gate 3 FAIL，見 `docs/2026-07-14-m2e-sunk-first-negative-result.md`**  
基線：`d5522b2`（sunk-first 防禦策略文件已提交）  
前置：`SplitM1GExactManager`、M2e-PR 負結果、PVGS `CompletionSweep -> DispatchLoop` 證據鏈

---

## 1. 問題與目標

目前 M2e 在單一快照內聯合決定 `q[i,o,p,s]`、pod selection、station assignment 與 bot assignment；PVGS 則先反覆榨取已承諾 pod，再一次只派一顆新 pod 並重掃 backlog。既有實驗顯示：M2e 與 PVGS 首波打包接近，closed-loop pile-on 差距主要來自 pod 駐留期間的 re-harvest。

本 SPEC 的唯一行為假說是：

> 在開放新 pod 之前，先把「完全由已承諾 pod 供給的母單」設成嚴格高優先級，可把 PVGS/HADGS 的 sunk-first 控制流內生化到 M2e 的單次 MILP 快照。

這是**可檢驗假說，不是閉環統治理論**。本設計只能保證每個決策快照在新定義的字典序目標下最優；WHCA*、到站時間、slot 回收與後續 backlog 仍可能使閉環 PVGS 勝出。

### 1.1 成功條件

以 `split_milp_m2ea.xconf` 的 aligned、未調價設定為母版，只新增 sunk-first 旗標。五 seeds 下，M2e-SF 必須同時不低於固定 PVGS-E 參考臂的：

1. 完成訂單吞吐 `StatOverallOrdersHandled`；
2. 系統 order pile-on `KPI_PO`；
3. 系統 item pile-on `KPI_IPO`。

策略文件中的 4.60/8.28 實際指 item pile-on，不得在報告中誤寫成 order pile-on。現有五 seed 實檔均值約為：M2e `642.4 / PO 2.051 / IPO 4.567`，PVGS-E `641.2 / PO 3.703 / IPO 8.168`。

### 1.2 非目標

- 不修改 M1G、HADGS、PVGS 或 `SplitM1GLBManager`。
- 不引入時間展開、pod retention 或 release-moment slot hold。
- 不在本版修改 Od admission 行為，只增加觀測。
- 不疊加 `m2e_C`、M2e-PR、CF、epsilon 或 processing-pod reward 來通過主驗收。
- 不聲稱 exact 在 closed loop 中理論保證優於 heuristic。

---

## 2. 名詞與既有集合

在決策 epoch `t`：

- `O`：本次通過現有 admission/urgent shrink 的 pending parent orders。
- `S`：目前有空 order slot 的 stations；`C_s` 是 station `s` 的空 slot 數。
- `I_o`：order `o` 的完整 `RemainingPositions` SKU 集合。
- `r_oi`：order `o` 對 SKU `i` 的剩餘未 claim 數量。
- `P_b`：已由 bot 認領且已固定 station 的 inherited pods，包括 processing、queueing、en-route；其 `x[p,sigma(p)]=1`、`y[rho(p),p]=1` 已由 `eshi7/eshi11` 固定。
- `P_a`：仍在 storage、可由空閒 bot 新認領的 pods。
- `q_iops`：order `o` 的 SKU `i` 從 pod `p` 在 station `s` claim 的件數。

本 SPEC 的 **sunk** 嚴格等於目前初始化產生的 `P_b`，不另用距離或 waypoint 推估。這可避免「正在站上」與「已在途」的分類在不同 manager 漂移，也與 PVGS `BuildEpochState` 將 inherited inbound pods 放入 station supply 的現況一致。

---

## 3. 核心決策

### D1：同一個模型，兩次 optimize，只提交第二次解

不建立兩套 demand ledger，也不在 Solve 1 後建立 child、扣庫存或 claim 新 pod。兩次求解共用同一組變數與限制式：

1. Solve 1 求 sunk-first 最優值；
2. 加入整數等式鎖；
3. Solve 2 恢復既有 M2e objective；
4. 只 decode/commit Solve 2 的 solution。

因此沒有中途 rollback、重建 `RemainingPositions` 或兩次 `CreateSplitChild` 的風險。

### D2：Sunk-first 獎勵「完整關單」，不獎勵 raw partial units

M2e-PR 已證明 raw partial reward 雖增加 partial assignments，卻造成 consolidation wait 爆增且 TP 不升。本設計只計算完全由 `P_b` 滿足的母單，不鎖任何未完成 order 的部分 `q`。

### D3：Order pile-on 第一，completed-item pile-on 第二

Solve 1 先最大化 sunk-completed parent order 數；同樣完成數下，再偏好完成 residual item 數較多的母單。item tie-break 僅計入**已完整關單**的 residual units，不鼓勵建立等待未來 pod 的 partial child。

### D4：Solve 2 保留既有 M2e objective

主實驗要隔離「求解順序」效果，因此 Solve 2 不重寫 `w1/w2/w3/w4/w5/epsilon/PR/CF` 的係數或語意。驗收 xconf 使用 `OrderRewardWeight=-40`、`IdleSlotWeight=0`，其餘可選 reward/cost 皆為 0。

### D5：Od 先觀測，後決策

本版只記錄 `odCount` 與 `odFired`。若觀測證明 Od replacement 實際頻繁發生，再另開 amendment 比較 `legacy/off/HADGS-sum`；不得把 Od 行為修正與 sunk-first 綁成同一個 causal arm。

---

## 4. 數學定義

保留目前 M2e 的全部可行域 `F`，包括 inventory、slot、pod-station、robot-pod、`eshi13'`、`eM2` 與既有 completion constraints。

新增 binary variable：

$$
h_o \in \{0,1\} \qquad \forall o\in O
$$

`h_o=1` 的精確語意：order `o` 的**全部剩餘 SKU**都由本次快照內的 `P_b` pods claim 完，不得靠 `P_a` 補足。

對每個完整 residual SKU：

$$
\sum_{p\in P_b\cap P_i}\sum_{s\in S}q_{iops}
\ge r_{oi}h_o
\qquad \forall o\in O,\ i\in I_o
\tag{SF1}
$$

若 `P_b ∩ P_i` 為空且 `r_oi>0`，直接加入：

$$
h_o\le0
\tag{SF1-OOS}
$$

這裡必須迭代完整 `residuals[o]`，不得使用 `.Where(PiSKU.ContainsKey)` 跳過缺貨線。

定義：

$$
H=\sum_{o\in O}h_o
$$

$$
R_o=\sum_{i\in I_o}r_{oi},
\qquad
I_H=\sum_{o\in O}R_oh_o
$$

`H` 是 sunk pods 可完整關閉的母單數；`I_H` 是這些關閉母單包含的 residual item 數。

為了維持**兩次** optimize，同時得到嚴格的 `H` 優先、`I_H` 次之，令：

$$
M_H=1+\sum_{o\in O}R_o
$$

Solve 1：

$$
\max_{F}\quad M_HH+I_H
\tag{SF-S1}
$$

因為任意兩解的 `I_H` 差不可能超過 `ΣR_o`，增加一張 sunk-completed order 必定勝過所有 item tie-break 差異。這是有界的字典序係數，不是待調參 weight。

取得整數最優值 `H*`、`I_H*` 後加入：

$$
H=H^*,\qquad I_H=I_H^*
\tag{SF-LOCK}
$$

Solve 2：

$$
\min_{F}\quad J_{M2e}
\qquad
\text{s.t. } H=H^*,\ I_H=I_H^*
\tag{SF-S2}
$$

`J_M2e` 就是旗標關閉時的既有 objective。

### 4.1 為何這等價於「先 sunk、再新派車」

既有 `eM2` 已有：

$$
\sum_{p\in P_a\cup P_b}\sum_s q_{iops}\le r_{oi}
$$

若 `h_o=1`，SF1 又要求 `P_b` 的供給量至少為 `r_oi`，因此該 SKU 必然滿足：

$$
\sum_{p\in P_b,s}q_{iops}=r_{oi},
\qquad
\sum_{p\in P_a,s}q_{iops}=0
$$

所以被 Solve 1 計入的母單在 Solve 2 不可能偷用新 pod。其餘 `h_o=0` orders 才能由既有與新 pod 混合服務，這就是模型內的 residual-demand 語意。

### 4.2 與真正完成事件的界線

`h_o=1` 代表所有剩餘需求在本次被 claim；實體 parent completion 仍要等所有歷史與本次 child 都揀完。論文與 log 應稱 `sunk-closed` 或 `fully claimed by sunk supply`，不得寫成「當下已完成事件」。真正 KPI 仍用 `StatOverallOrdersHandled`。

---

## 5. Config 與交互作用

在 `SplitM1GExactConfiguration` 最後新增：

```csharp
/// <summary>
/// M2e-SF: before the legacy objective, lexicographically maximize parent orders
/// fully supplied by inherited Pb pods, then their residual item count.
/// Active only when CrossTime=true. Default false is behavior-identical.
/// </summary>
public bool SunkFirstScoring = false;
```

行為矩陣：

| 條件 | 行為 |
|---|---|
| `SunkFirstScoring=false` | 不建 `h`、不做額外 optimize；既有路徑 bit-identical |
| `true` + `CrossTime=true` | 啟用 SF-S1、鎖值、SF-S2 |
| `true` + `CrossTime=false` | 旗標 inert，M1e bit-identical |
| `true` + `CoverageFirstScoring=true` | fail fast；兩者都擁有 Solve 1，禁止未定義的雙重優先 |
| PR/epsilon/w4/w5 非零 | 技術上只作用於 Solve 2；不屬於主驗收，必須另列 interaction arm |
| PVGS/LB config 繼承到欄位 | manager 不讀取，無行為 |

主驗收新增 `split_milp_m2e_sunk.xconf`，逐欄複製 `split_milp_m2ea.xconf`，只多：

```xml
<SunkFirstScoring>true</SunkFirstScoring>
```

XML element 必須遵守欄位宣告順序。

---

## 6. 診斷與 log contract

在 `splitm1gx_decision_log.csv` 既有 15 欄後追加：

| 欄位 | 意義 |
|---|---|
| `odCount` | `GenerateOdSplit` 回傳數量 |
| `odFired` | `odCount > Σ_s C_s`，是否真的替換 pending pool |
| `sunkOrdersStar` | `H*`；旗標關閉為 0 |
| `sunkItemsStar` | `I_H*`；旗標關閉為 0 |
| `sunkUnitsFinal` | Solve 2 最終解中從 `P_b` claim 的 `Σq` |
| `newTripsFinal` | Solve 2 最終解中 `P_a` 的 `Σx` |
| `solve1Sec` | SF Solve 1 時間；旗標關閉為 0 |
| `solve2Sec` | 最終 solve 時間；原 `solveSec` 保留為總時間 |

必要一致性檢查：

- `sunkOrdersStar <= final true-sunk-completed count`，否則鎖值或 decode 有錯。
- `h_o=1` 的每一條 final `q` 都不得來自 `P_a`。
- 旗標關閉時，新增 log 只能是 0/觀測值，不得改變既有前 15 欄的決策內容。

Od probe 先用 `split_milp_m2ea.xconf` seed 0 跑一次，報告 `odFired decisions / total decisions` 與 fired 時的 order/slot 分布；本 SPEC 不預設 firing-rate 門檻，由量測結果決定是否開 Od amendment。

---

## 7. 實作邊界

預計修改：

1. `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`
   - 新增 default-off `SunkFirstScoring`。
2. `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs`
   - 建立 `h_o`；加入 SF1/SF1-OOS；建立 Solve 1 objective；整數鎖值；Solve 2；log 欄位。
3. `Material/Instances/CoreBenchmark/small/split_milp_m2e_sunk.xconf`
   - aligned baseline + 單一旗標。
4. `RAWSimO.Tests`
   - 新增純邏輯測試或微型模型測試，覆蓋下列不變量。

明確禁止修改：

- `M1GManager.cs`
- `HADGSManager.cs`
- `PVGSManager.cs`
- `SplitM1GLBManager.cs`（目前工作樹另有未提交修改）
- 既有 acceptance xconf

---

## 8. 驗證 Gate

### Gate 0：回歸與可逆性

1. 變更前先保存 `split_milp_m2ea.xconf` seed 0 baseline。
2. 旗標關閉重跑，完成訂單、items、arrivals、距離、能耗與既有 decision-log 前 15 欄必須 deterministic-equivalent。
3. M1e flag-on smoke 必須等同 flag-off，證明 CrossTime gate。

### Gate 1：語意測試

至少涵蓋：

1. 一張單只有 `P_b` 能完整供給，另一張需 `P_a`，Solve 1 必須選前者。
2. 相同 `H*` 下，選擇 residual item 數較大的完整母單。
3. 任一 residual SKU 無 `P_b` 候選時，`h_o=0`。
4. `h_o=1` 的 final solution 對 `P_a` draw 必須為 0。
5. 保持 `H*` 後，Solve 2 仍可為其他 order 開啟 `P_a` trip。
6. SF+CF 同開時明確失敗，不允許悄悄選 precedence。

### Gate 2：單 seed 機制證據

使用相同 setting/seed 跑 baseline、M2e-SF、PVGS-E：

- `sunkOrdersStar` 必須實際大於 0 且分布合理；若近乎永遠為 0，sunk-first lever 未被觸發，停止五 seed。
- `sunkOrdersStar` 鎖值零違反。
- 報告 `sunkUnitsFinal/newTripsFinal`、PO、IPO、TP、order residency、slot occupancy。
- 同時完成 Od firing-rate 報告，但不改 Od 行為。

### Gate 3：五 seed 硬驗收

固定 arms：

- M2e baseline：`split_milp_m2ea.xconf`
- M2e-SF：`split_milp_m2e_sunk.xconf`
- PVGS reference：`pvgs_e.xconf`

硬條件採 paired five-seed means：

$$
\overline{TP}_{SF}\ge\overline{TP}_{PVGS-E}
$$

$$
\overline{PO}_{SF}\ge\overline{PO}_{PVGS-E}
$$

$$
\overline{IPO}_{SF}\ge\overline{IPO}_{PVGS-E}
$$

並完整列出 RD、OD、EOR、late rate、consolidation wait、decision solve-time median/p95/max 與各 seed，不得只報均值。

線上求解 guardrail：SF 的 decision solve-time median 與 p95 原則上不得超過 baseline 的 2.5 倍；超過時即使 KPI 通過，也必須在 scalability 小節標記成本，不可宣稱為可擴展 online policy。

### Gate 4：失敗處置

若 TP/PO/IPO 任一未通過：

1. 不做 sunk weight sweep，因本設計沒有可調 sunk weight。
2. 先用 log 判定是 `H*` 近零、slot 被 sunk lock 佔滿、或 en-route `P_b` 提前綁單。
3. 將結果記為第二個結構性負結果，轉向已量化的 release-moment pivotal retention；不得再以 objective reward 調參掩蓋。

---

## 9. 已知風險

1. `P_b` 包含 en-route pod，不只當下 processing pod。Sunk-first 可能提早占 slot，重現 order residency/slot ceiling；因此 Gate 2 必須量 residency 與 occupancy。
2. 嚴格 `H*` 優先可能犧牲需要一顆新 pod 即可完成的多張訂單。這是模仿 HADGS/PVGS sunk-first 節奏的刻意代價，也可能令 TP 下降。
3. `I_H` 偏好 residual 較大的完整單，可能與 deadline tie-break 衝突；urgent pool admission 維持既有行為，未另加 urgency objective。
4. Solve 1 與 Solve 2 共用 slot/inventory constraints，但第一解不提交。任何實作若在 Solve 1 後讀解並改 simulator state，都違反 D1。
5. 即使每個 epoch 的 sunk-first objective exact-optimal，閉環仍可能輸給 one-claim-resweep。驗收結果才是主張邊界。

---

## 10. 待審查決策

進 implementation plan 前，需逐項確認：

1. `P_b` 是否維持 processing + queueing + en-route 全納入，或只限定 processing pod。
2. item 次級層是否接受目前的「completed residual items」定義，而不是 raw `Σq(P_b)`。
3. 主參考臂是否固定為 `pvgs_e.xconf`；若論文主表使用 regular `pvgs_m2e.xconf`，需另列第四臂，不能混用兩者的 TP 與 IPO 數字。
4. 五 seed 硬門檻是否確定要求 TP、PO、IPO 三者皆不低於參考臂。
5. Od firing rate 出來後，是否另開 `OdMode` amendment。
