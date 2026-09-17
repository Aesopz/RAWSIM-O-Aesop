# 2026-09-17_large_60b_dyn_vs_fix13_m5

- 類別：快速驗證（1 seed）
- 產生：2026-09-17T21:21:47・正典 v2・DLL 2b11c291972a・git 5010eea961
- 場次狀態：valid=2

## 為何做這組實驗

**問題**：60 台（5 bots/station）大規模：動態 vs 固定 λ=13，機器人更充裕時定價的效率空間是否出現？

**動機**：小規模 6→10 台顯示機器人越充裕、動態的效率優勢越大（距離 16%→41%）；大規模 45 台（3.75/站）未見此效應。以 60 台（5/站，與小規模 10b 同比）1 seed 探風向。μ 沿用 45 台動態 μ/λ 中位 1.6208。

## 設定

- **m5_dyn_60b**：hgs_m5_k10.xconf・GreedyM5Configuration・seeds [0]
  - 60 bots・2 h · 1 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/large_45b_12p_6r.xlayo`：`-  <BotCount>45</BotCount>`；`+  <BotCount>60</BotCount>`
- **m5_fix13_60b**：m5_fix13_60b.xconf・GreedyM5Configuration・seeds [0]
  - 60 bots・2 h · 1 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/large_45b_12p_6r.xlayo`：`-  <BotCount>45</BotCount>`；`+  <BotCount>60</BotCount>`
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>13</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>21.0704</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`

## 結果

```
2 h · 1 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_dyn_60b,60b,8318.0,5402.0,3436.0,7.373,466.0,0.1356,20.20,3.428,382.6,3.78
m5_fix13_60b,60b,8343.0,5396.0,3404.0,8.066,422.0,0.1240,19.30,3.405,369.9,3.52

excel cross-check: {"status": "passed", "checks": 20, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

seed 0：60b 固定 13 vs 動態 → 件數 +0.3%、pile-on +9.4%、trips −9.4%、m/line −4.4%（45b 同 seed −2.4%）、EOR −0.7%。45→60 台件數不變（8,3xx）：大規模 45 台已是站台瓶頸，多的車是閒車。
結論：「機器人充裕→動態效率優勢放大」的小規模規律在大規模不成立且反向；原因是小規模的固定高價會過度派車（10b trips +60%）、大規模則是動態（λ*≈8＋λ̄ 補價）多派。效率空間方向由「誰在多派車」決定。後續工作：λ̄ 跳躍應以站台飢餓為條件。1 seed 足以定方向（效應 4–9% ≫ seed 噪音）。
