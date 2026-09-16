# 2026-09-16_m4g_legacy_objective_vs_m4g

- 類別：正式實驗（10 seeds）
- 產生：2026-09-16T02:09:19・正典 v1・DLL 94a44bdb4844・git retro
- 場次狀態：valid=40

## 為何做這組實驗

**問題**：同一可行解區域（M4G 限制式，允許行級拆單）下，Jiao 的固定權重目標式（w1=1, w2=−40, w3=1000）相對本研究的比值目標式（λ/μ/β 量測）表現如何？

**動機**：Appendix 素材：說明本研究目標式優於直接套用 Jiao 權重；消融鏈中「同可行域只換目標式」的乾淨對照。M4G 對照組沿用既有 Canon v1 資料。

## 設定

- **m4g_6b**：m4g.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m4g_legacy_6b**：m4g_legacy.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`
- **m4g_10b**：m4g.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m4g_legacy_10b**：m4g_legacy.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`

比較：
- m4g_6b vs m4g_legacy_6b（ablation；變數 objective: Jiao weights (1, -40, 1000) vs measured ratio prices; same feasible region）
- m4g_10b vs m4g_legacy_10b（ablation；變數 objective: Jiao weights (1, -40, 1000) vs measured ratio prices; same feasible region）

## 結果

```
2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m4g_6b,6b,1419.9,946.1,599.1,5.338,112.3,0.1875,9.37,1.190,830.8,1.45
m4g_legacy_6b,6b,1407.8,939.5,589.7,4.414,133.6,0.2266,11.17,1.429,858.9,2.32
m4g_10b,10b,1428.7,952.2,603.5,5.118,118.0,0.1956,10.35,1.317,787.8,0.86
m4g_legacy_10b,10b,1428.1,959.2,602.5,2.939,205.1,0.3405,15.69,2.039,736.2,0.91

paired m4g_6b vs m4g_legacy_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.86,+0.70,+1.59,+20.91,−15.94,−17.24,−16.10,−16.77,−3.27,−37.25
p,.005,.085,.006,< .001,< .001,< .001,< .001,< .001,.092,.004
sig,**,,**,***,***,***,***,***,,**

paired m4g_10b vs m4g_legacy_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.04,−0.73,+0.17,+74.11,−42.47,−42.56,−34.02,−35.41,+7.01,−6.01
p,.239,.048,.665,< .001,< .001,< .001,< .001,< .001,.051,.107
sig,,*,,***,***,***,***,***,,

excel cross-check: {"status": "passed", "checks": 120, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

- 結論：同一可行域（M4G 限制式，允許行級拆單）下，Jiao 的固定權重目標式（1, −40, 1000；w2 掛估值變數，隱含 β≈0.038）拿到與 M4G 幾乎相同的吞吐（件數 6b +0.86%**、10b n.s.），但效率明顯較差：M4G 相對 Legacy 趟次 −15.9%***（6b）/ −42.5%***（10b），EOR −16.8%*** / −35.4%***，pile-on +20.9%*** / +74.1%***。
- 機制：Legacy 的 −40 完成獎勵永遠壓過幾公尺距離，機器人越多越常派出低效率趟次；10b 時 Legacy 的 pile-on 2.94、趟次 205 幾乎回到 M1G（3.02、213）水準。比值目標式以「每單位進度的公尺」計價，才會拒絕這些趟次。
- Turnover 兩者無顯著差異（−3.3% / +7.0% n.s.）：定價不額外拉長週期。
- 拆單比例相近（seed 0：Legacy 41%、M4G 38% 的訂單被拆），差異來自目標式而非拆單多寡。
- 可信度：10 seeds、40/40 valid、Canon v1；Legacy 用 bin_legacydist（新增 LegacyDistanceWeight 預設 1），守門員 2026-09-16_legacydist_guard 證明與 Release 逐位相同；Excel 交叉驗證通過。
- 用途：Appendix「採用 Jiao 權重」對照；消融鏈中「同可行域只換目標式」的乾淨一段。口徑：Jiao's weights recover the throughput of splitting but not its efficiency.
