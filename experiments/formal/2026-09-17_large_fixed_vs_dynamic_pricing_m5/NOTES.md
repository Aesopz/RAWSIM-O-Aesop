# 2026-09-17_large_fixed_vs_dynamic_pricing_m5

- 類別：正式實驗（10 seeds）
- 產生：2026-09-17T15:00:33・正典 v2・DLL 94a44bdb4844・git retro
- 場次狀態：valid=70

## 為何做這組實驗

**問題**：大規模（45 台）HGS-M5 下，Dinkelbach 動態定價是否優於事後最佳的固定 λ/μ？

**動機**：S1 的大規模對應版。格點錨定 Canon v2 M5 動態組 4,483 次派車決策的 λ_end 分位（P10 4.3／P25 6.4／P50 8.3／P75 12.7／P90 45.5）；μ 依動態 μ/λ 中位 1.6208 同步固定；β 不固定；LambdaIterations=0、UpperBoundJump=false。動態組沿用 2026-09-16_canon_v2_large_split_limit_m5/m5_45b。

## 設定

- **m5_dyn_45b**：hgs_m5_k10.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
- **m5_fix4_45b**：m5_fix4_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>4</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>6.4832</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix6_45b**：m5_fix6_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>6</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>9.7248</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix8_45b**：m5_fix8_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>8</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>12.9664</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix13_45b**：m5_fix13_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>13</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>21.0704</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix20_45b**：m5_fix20_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>20</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>32.416</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix45_45b**：m5_fix45_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>45</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>72.936</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`

比較：
- m5_fix4_45b vs m5_dyn_45b（ablation；變數 fixed lambda=4, mu=6.4832, no lambda iteration, no bound jump）
- m5_fix6_45b vs m5_dyn_45b（ablation；變數 fixed lambda=6, mu=9.7248, no lambda iteration, no bound jump）
- m5_fix8_45b vs m5_dyn_45b（ablation；變數 fixed lambda=8, mu=12.9664, no lambda iteration, no bound jump）
- m5_fix13_45b vs m5_dyn_45b（ablation；變數 fixed lambda=13, mu=21.0704, no lambda iteration, no bound jump）
- m5_fix20_45b vs m5_dyn_45b（ablation；變數 fixed lambda=20, mu=32.416, no lambda iteration, no bound jump）
- m5_fix45_45b vs m5_dyn_45b（ablation；變數 fixed lambda=45, mu=72.936, no lambda iteration, no bound jump）

## 結果

```
2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_dyn_45b,45b,8318.2,5396.9,3385.3,7.625,444.1,0.1312,20.45,3.601,395.2,3.79
m5_fix4_45b,45b,7399.2,4785.8,2991.0,7.984,374.7,0.1253,19.92,3.543,399.7,14.41
m5_fix6_45b,45b,7961.5,5156.3,3236.6,8.091,400.2,0.1237,19.74,3.469,383.8,7.92
m5_fix8_45b,45b,8223.2,5332.0,3339.4,8.195,407.8,0.1221,19.55,3.468,380.9,4.89
m5_fix13_45b,45b,8370.2,5421.4,3400.0,8.136,418.2,0.1230,19.92,3.552,388.2,3.20
m5_fix20_45b,45b,8372.4,5426.1,3403.2,8.113,419.7,0.1233,20.16,3.618,397.5,3.17
m5_fix45_45b,45b,8342.5,5411.0,3404.9,8.362,407.3,0.1196,20.32,3.656,387.6,3.51

paired m5_fix4_45b vs m5_dyn_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−11.05,−11.32,−11.65,+4.71,−15.63,−4.50,−2.60,−1.60,+1.14,+280.47
p,< .001,< .001,< .001,< .001,< .001,< .001,.019,.088,.274,< .001
sig,***,***,***,***,***,***,*,,,***

paired m5_fix6_45b vs m5_dyn_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−4.29,−4.46,−4.39,+6.11,−9.89,−5.74,−3.48,−3.66,−2.87,+109.06
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,.037,< .001
sig,***,***,***,***,***,***,***,***,*,***

paired m5_fix8_45b vs m5_dyn_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−1.14,−1.20,−1.36,+7.47,−8.17,−6.90,−4.38,−3.70,−3.61,+29.04
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,.003,< .001
sig,***,***,***,***,***,***,***,***,**,***

paired m5_fix13_45b vs m5_dyn_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.63,+0.45,+0.43,+6.70,−5.83,−6.24,−2.57,−1.35,−1.77,−15.62
p,.001,.053,.211,< .001,< .001,< .001,.006,.096,.145,.001
sig,**,,,***,***,***,**,,,**

paired m5_fix20_45b vs m5_dyn_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.65,+0.54,+0.53,+6.40,−5.49,−5.98,−1.42,+0.48,+0.59,−16.43
p,< .001,.006,.099,< .001,< .001,< .001,.038,.530,.627,< .001
sig,***,**,,***,***,***,*,,,***

paired m5_fix45_45b vs m5_dyn_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.29,+0.26,+0.58,+9.66,−8.29,−8.81,−0.64,+1.54,−1.92,−7.35
p,.019,.314,.157,< .001,< .001,< .001,.451,.063,.088,.023
sig,*,,,***,***,***,,,,*

excel cross-check: {"status": "passed", "checks": 260, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

**結論（10 seeds、雙尾配對 t）**：大規模下動態交換率**不再壓倒性勝出**。
- 低固定 λ（4／6／8）明顯輸：件數 −11.0／−4.3／−1.1%（p<.001），站台閒置 +10.6／+4.1／+1.1 pp，pod trips −8～−16%（車跑太少）。
- 高固定 λ（13／20／45）與動態打平或微勝：件數 +0.3～+0.7%（p<.05～.001，但效應 <1%），m/line −0.6～−2.6%，EOR −1.4～+1.5%（皆 n.s.），pile-on +6～10%（固定高價逼出更高 pile-on），turnover n.s.。
- 圖 A：動態點落在 λ=13/20/45 叢集內、略高於 λ=13；沒有任何固定 λ 在動態的左上（更差）以外之處明顯佔優——但 λ=13 在右下（更多件、更短距離），差距 0.6%／2.6%。

**解讀**：大規模 45 台、12 站，機器人不再是稀缺瓶頸，λ 高低對吞吐的邊際影響很小；動態的價值從「贏過最佳常數」變成「不需要知道最佳常數在哪」——事後最佳常數（13）要先掃描才知道，而 4～8 這些「看起來合理」的常數（動態 λ_end 中位數 8.3）會輸 1～11%。這與小規模結論一致的部分是：**低價致命**；不一致的部分是：大規模高價不再浪費（trips −5～−8% 反而少）。

**與小規模對照**：小規模 M4G 動態全勝（EOR 13～28%），大規模 M5 動態 ≈ 最佳常數。寫作時不要宣稱「動態永遠最好」，改寫成「動態達到事後最佳常數的水準且無需調參；固定低價在兩個規模都顯著劣化」。

**可信度**：動態組沿用 canon v2 m5_45b（guard 證明逐位相同）；固定組 60 場新跑；同 DLL 語意；excel 交叉驗證通過。

**補充（fast 2026-09-17_large_dyn_nojump_m5）**：關閉 UpperBoundJump 後件數 −24%、閒置 +23 pp，故差距不是跳躍過度派車，而是 λ* 偏保守需離散補價 vs 常數中價平順派車。

**下一步**：口試投影片大規模定價頁用 figA/figB_45b；文字用上面的措辭。
