# 2026-09-18_small_pure_m5_vs_m4g_fidelity

- 類別：正式實驗（10 seeds）
- 產生：2026-09-18T17:48:54・正典 v2・DLL 94a44bdb4844・git retro
- 場次狀態：valid=60

## 為何做這組實驗

**問題**：純貪婪 HGS-M5（無價格）在小規模是否仍忠實追隨 M4G（精確解）？

**動機**：主線改為純貪婪 HGS-M5；原保真度（M5-λ vs M4G，差 <0.2%）不再對應主線政策。以純貪婪版重做 6b/10b × 10 seeds；M4G 與 M5-λ 沿用 2026-09-15_canon_v1_small_split_limit_fidelity（guard 逐位）。

## 設定

- **m4g_6b**：m4g.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m5_lambda_6b**：hgs_m5.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m5_pure_6b**：m5_pure_6b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m4g_10b**：m4g.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m5_lambda_10b**：hgs_m5.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m5_pure_10b**：m5_pure_10b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`

比較：
- m5_pure_6b vs m4g_6b（fidelity；變數 solver: exact MILP -> pure greedy）
- m5_pure_6b vs m5_lambda_6b（ablation；變數 remove Dinkelbach price）
- m5_pure_10b vs m4g_10b（fidelity；變數 solver: exact MILP -> pure greedy）
- m5_pure_10b vs m5_lambda_10b（ablation；變數 remove Dinkelbach price）

## 結果

```
2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m4g_6b,6b,1419.9,946.1,599.1,5.338,112.3,0.1875,9.37,1.190,830.8,1.45
m5_lambda_6b,6b,1422.2,931.2,584.0,5.231,111.7,0.1913,9.40,1.203,916.9,1.30
m5_pure_6b,6b,1421.1,930.9,593.0,5.873,101.0,0.1703,9.40,1.165,890.7,1.39
m4g_10b,10b,1428.7,952.2,603.5,5.118,118.0,0.1956,10.35,1.317,787.8,0.86
m5_lambda_10b,10b,1430.3,934.2,588.1,5.081,115.8,0.1970,10.18,1.312,955.2,0.74
m5_pure_10b,10b,1428.6,937.9,593.9,5.681,104.6,0.1762,10.26,1.281,902.2,0.87

paired m5_pure_6b vs m4g_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.08,−1.61,−1.02,+10.04,−10.06,−9.17,+0.27,−2.08,+7.21,−4.11
p,.695,.006,.114,< .001,< .001,< .001,.764,.047,.024,.774
sig,,**,,***,***,***,,*,*,

paired m5_pure_6b vs m5_lambda_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.08,−0.03,+1.54,+12.29,−9.58,−10.94,−0.03,−3.19,−2.86,+7.31
p,.634,.856,.009,< .001,< .001,< .001,.976,.016,.246,.543
sig,,,**,***,***,***,,*,,

paired m5_pure_10b vs m4g_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.01,−1.50,−1.59,+11.02,−11.36,−9.92,−0.90,−2.76,+14.52,+1.27
p,.904,.001,.014,< .001,< .001,< .001,.310,.035,.007,.831
sig,,**,*,***,***,***,,*,**,

paired m5_pure_10b vs m5_lambda_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.12,+0.40,+0.99,+11.82,−9.67,−10.55,+0.72,−2.38,−5.55,+17.86
p,< .001,.029,.002,< .001,< .001,< .001,.293,.008,.114,< .001
sig,***,*,**,***,***,***,,**,,***

excel cross-check: {"status": "passed", "checks": 200, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

**結論**：純貪婪 HGS-M5 在小規模與 M4G（精確解）件數持平（6b +0.08、10b −0.01，n.s.），且效率更好：EOR −2.1%*／−2.8%*、trips −10%***／−11%***、pile-on +10%***／+11%***；代價 turnover +7.2%*／+14.5%**、lines −1.5～−1.6%**。閒置無差。
**vs HGS-M5-λ**：件數 −0.1（6b n.s.、10b −0.12%***），EOR −3.2%*／−2.4%**，pile-on +12%***——拿掉價格在小規模反而更省。
主線保真度主張：「純貪婪 HGS-M5 件數與精確解無差、能耗更低 2–3%、以週轉時間 +7～15% 為代價」。
可信度：10 seeds、雙尾配對 t、excel 交叉驗證通過；M4G／M5-λ 沿用 guard 逐位的既有 run。
圖：figures/fig_m5_vs_m4g_small.png、fig_m5_vs_m5lambda_small.png（F1–F12）。
