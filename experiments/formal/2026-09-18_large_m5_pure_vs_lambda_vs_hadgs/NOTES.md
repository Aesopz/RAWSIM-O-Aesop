# 2026-09-18_large_m5_pure_vs_lambda_vs_hadgs

- 類別：正式實驗（10 seeds）
- 產生：2026-09-18T02:12:30・正典 v2・DLL 2b11c291972a・git c4f718664f
- 場次狀態：valid=30

## 為何做這組實驗

**問題**：大規模純貪婪 HGS-M5（無價格）對 HADGS 與 HGS-M5-λ 的 10 seeds 配對對照

**動機**：報告主線改為純貪婪 HGS-M5（大道至簡）；需要它對 HADGS 的正式數字，以及對 HGS-M5-λ 的差距（價格值多少）。seed 0 fast 顯示 +23.5% 件 vs HADGS、−3.2% vs λ 版。

## 設定

- **m5_pure_45b**：m5_pure_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_lambda_45b**：hgs_m5_k10.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
- **hadgs_45b**：hadgs_aligned.xconf・HADGSConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock

比較：
- m5_pure_45b vs hadgs_45b（benchmark；變數 policy: HADGS -> HGS-M5 (pure greedy, splitting)）
- m5_lambda_45b vs m5_pure_45b（ablation；變數 add Dinkelbach price (lambda, mu, beta) + bound jump）
- m5_lambda_45b vs hadgs_45b（benchmark；變數 policy: HADGS -> HGS-M5-λ）

## 結果

```
2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_pure_45b,45b,8151.3,5289.6,3323.4,8.362,397.5,0.1196,21.57,3.938,399.0,5.71
m5_lambda_45b,45b,8318.2,5396.9,3385.3,7.625,444.1,0.1312,20.45,3.601,395.2,3.79
hadgs_45b,45b,6340.9,4100.2,2788.6,2.275,1225.8,0.4398,41.28,7.738,176.5,26.68

paired m5_pure_45b vs hadgs_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+28.55,+29.01,+19.18,+267.57,−67.57,−72.81,−47.75,−49.11,+126.10,−78.58
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,***,***,***,***,***,***,***,***

paired m5_lambda_45b vs m5_pure_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+2.05,+2.03,+1.86,−8.82,+11.72,+9.68,−5.21,−8.55,−0.97,−33.70
p,< .001,< .001,.001,< .001,< .001,< .001,< .001,< .001,.416,< .001
sig,***,***,**,***,***,***,***,***,,***

paired m5_lambda_45b vs hadgs_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+31.18,+31.63,+21.40,+235.15,−63.77,−70.17,−50.47,−53.46,+123.92,−85.80
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,***,***,***,***,***,***,***,***

excel cross-check: {"status": "passed", "checks": 120, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

**主線數字（純貪婪 HGS-M5 vs HADGS，10 seeds）**：件數 +28.6%***、訂單 +19.2%***、pile-on 2.28→8.36、trips −67.6%***、m/line −47.8%***、EOR −49.1%***、閒置 26.7%→5.7%；turnover +126%（拆單合併等待，已知代價）。
**價格值多少（HGS-M5 → HGS-M5-λ）**：件數 +2.1%***、m/line −5.2%***、EOR −8.6%***、閒置 5.7%→3.8%；代價 trips +11.7%、pile-on −8.8%。
**拆解 HADGS→HGS-M5-λ 的 +31.2%**：28.6 pp 結構（拆單＋線級貪婪）、2.6 pp 價格。使用者假設「M5 贏 HADGS 因為拆單」成立且比 seed 0 估計更強。
可信度：純貪婪 10 場新跑；λ 版沿用 canon v2（guard 逐位）；HADGS 沿用 out/mp1_hadgs_s0–9；同 seed 配對；excel 交叉驗證通過。
下一步：主線投影片以本表為大規模結果；HGS-M5-λ 放 future work（見 docs/2026-09-18-m5-pure-greedy-explained.html §8）。
