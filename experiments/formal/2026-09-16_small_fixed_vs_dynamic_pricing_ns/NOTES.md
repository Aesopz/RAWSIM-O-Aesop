# 2026-09-16_small_fixed_vs_dynamic_pricing_ns

- 類別：正式實驗（10 seeds）
- 產生：2026-09-16T09:11:32・正典 v1・DLL 94a44bdb4844・git 06a384206e
- 場次狀態：valid=220

## 為何做這組實驗

**問題**：Canon v1 下，M4G-NS 的動態定價（Dinkelbach）是否優於事後最佳的固定 λ/μ？固定價格在低 λ 區的停擺比例為何？

**動機**：第 24/27 頁定價論證用舊正典且格點未涵蓋動態 λ 工作區；重做以 M4G-NS（消融鏈一致）、真固定（關閉上界跳躍）、格點錨定動態派車 λ 分位（6b P10/P50/P90=2.59/4.31/7.68；10b 2.86/4.50/8.66）。μ 依動態組 μ/λ 中位比例（6b 1.3653、10b 1.4017）同步固定；β 不固定。另設保留跳躍的三點對照堵『故意讓固定組停擺』。動態組沿用既有 tau10_ns（Canon v1 等價）。

## 設定

- **ns_dyn_6b**：m4g_ns.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **ns_dyn_10b**：m4g_ns.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **ns_fix4p5_6b**：m4g_ns_fix4p5_6b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>4.5</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>6.1438</MuFixed>`
- **ns_fix6_6b**：m4g_ns_fix6_6b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>6</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>8.1918</MuFixed>`
- **ns_fix8_6b**：m4g_ns_fix8_6b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>8</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>10.9224</MuFixed>`
- **ns_fix11_6b**：m4g_ns_fix11_6b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>11</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>15.0183</MuFixed>`
- **ns_fix15_6b**：m4g_ns_fix15_6b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>15</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>20.4795</MuFixed>`
- **ns_fix20_6b**：m4g_ns_fix20_6b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>20</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>27.306</MuFixed>`
- **ns_fix28_6b**：m4g_ns_fix28_6b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>28</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>38.2284</MuFixed>`
- **ns_fix4p5_6b_jump**：m4g_ns_fix4p5_6b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>4.5</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`+    <MuFixed>6.1438</MuFixed>`
- **ns_fix8_6b_jump**：m4g_ns_fix8_6b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>8</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`+    <MuFixed>10.9224</MuFixed>`
- **ns_fix15_6b_jump**：m4g_ns_fix15_6b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>15</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`+    <MuFixed>20.4795</MuFixed>`
- **ns_fix4p5_10b**：m4g_ns_fix4p5_10b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>4.5</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>6.3076</MuFixed>`
- **ns_fix6_10b**：m4g_ns_fix6_10b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>6</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>8.4102</MuFixed>`
- **ns_fix8_10b**：m4g_ns_fix8_10b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>8</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>11.2136</MuFixed>`
- **ns_fix11_10b**：m4g_ns_fix11_10b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>11</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>15.4187</MuFixed>`
- **ns_fix15_10b**：m4g_ns_fix15_10b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>15</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>21.0255</MuFixed>`
- **ns_fix20_10b**：m4g_ns_fix20_10b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>20</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>28.034</MuFixed>`
- **ns_fix28_10b**：m4g_ns_fix28_10b_nojump.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>28</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>39.2476</MuFixed>`
- **ns_fix4p5_10b_jump**：m4g_ns_fix4p5_10b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>4.5</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`+    <MuFixed>6.3076</MuFixed>`
- **ns_fix8_10b_jump**：m4g_ns_fix8_10b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>8</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`+    <MuFixed>11.2136</MuFixed>`
- **ns_fix15_10b_jump**：m4g_ns_fix15_10b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>15</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`+    <MuFixed>21.0255</MuFixed>`

比較：
- ns_fix4p5_6b vs ns_dyn_6b（ablation；變數 fixed lambda=4.5, mu=6.1438, no bound jump）
- ns_fix6_6b vs ns_dyn_6b（ablation；變數 fixed lambda=6, mu=8.1918, no bound jump）
- ns_fix8_6b vs ns_dyn_6b（ablation；變數 fixed lambda=8, mu=10.9224, no bound jump）
- ns_fix11_6b vs ns_dyn_6b（ablation；變數 fixed lambda=11, mu=15.0183, no bound jump）
- ns_fix15_6b vs ns_dyn_6b（ablation；變數 fixed lambda=15, mu=20.4795, no bound jump）
- ns_fix20_6b vs ns_dyn_6b（ablation；變數 fixed lambda=20, mu=27.306, no bound jump）
- ns_fix28_6b vs ns_dyn_6b（ablation；變數 fixed lambda=28, mu=38.2284, no bound jump）
- ns_fix4p5_6b_jump vs ns_dyn_6b（ablation；變數 fixed lambda=4.5, mu=6.1438）
- ns_fix8_6b_jump vs ns_dyn_6b（ablation；變數 fixed lambda=8, mu=10.9224）
- ns_fix15_6b_jump vs ns_dyn_6b（ablation；變數 fixed lambda=15, mu=20.4795）
- ns_fix4p5_10b vs ns_dyn_10b（ablation；變數 fixed lambda=4.5, mu=6.3076, no bound jump）
- ns_fix6_10b vs ns_dyn_10b（ablation；變數 fixed lambda=6, mu=8.4102, no bound jump）
- ns_fix8_10b vs ns_dyn_10b（ablation；變數 fixed lambda=8, mu=11.2136, no bound jump）
- ns_fix11_10b vs ns_dyn_10b（ablation；變數 fixed lambda=11, mu=15.4187, no bound jump）
- ns_fix15_10b vs ns_dyn_10b（ablation；變數 fixed lambda=15, mu=21.0255, no bound jump）
- ns_fix20_10b vs ns_dyn_10b（ablation；變數 fixed lambda=20, mu=28.034, no bound jump）
- ns_fix28_10b vs ns_dyn_10b（ablation；變數 fixed lambda=28, mu=39.2476, no bound jump）
- ns_fix4p5_10b_jump vs ns_dyn_10b（ablation；變數 fixed lambda=4.5, mu=6.3076）
- ns_fix8_10b_jump vs ns_dyn_10b（ablation；變數 fixed lambda=8, mu=11.2136）
- ns_fix15_10b_jump vs ns_dyn_10b（ablation；變數 fixed lambda=15, mu=21.0255）

## 結果

```
2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
ns_dyn_6b,6b,1147.3,748.1,523.2,3.083,169.8,0.3252,14.84,1.746,435.5,20.37
ns_dyn_10b,10b,1411.2,919.0,634.5,3.208,198.2,0.3125,15.06,1.804,428.8,2.05
ns_fix4p5_6b,6b,1062.2,692.7,485.8,3.534,137.9,0.2838,14.23,1.656,455.0,26.25
ns_fix6_6b,6b,1152.2,752.0,523.3,3.520,148.7,0.2843,13.98,1.643,442.5,20.02
ns_fix8_6b,6b,1197.7,780.8,543.6,3.617,150.4,0.2767,13.59,1.587,452.1,16.88
ns_fix11_6b,6b,1235.5,806.9,561.6,3.599,156.0,0.2779,13.59,1.589,438.2,14.23
ns_fix15_6b,6b,1241.4,810.1,564.1,3.657,154.4,0.2740,13.54,1.577,438.9,13.84
ns_fix20_6b,6b,1238.9,811.3,565.6,3.643,155.3,0.2748,13.66,1.586,442.2,14.03
ns_fix28_6b,6b,1249.6,816.5,569.1,3.675,154.9,0.2724,13.66,1.587,434.0,13.28
ns_fix4p5_6b_jump,6b,1062.2,692.7,485.8,3.534,137.9,0.2838,14.23,1.656,455.0,26.25
ns_fix8_6b_jump,6b,1197.7,780.8,543.6,3.617,150.4,0.2767,13.59,1.587,452.1,16.88
ns_fix15_6b_jump,6b,1241.4,810.1,564.1,3.657,154.4,0.2740,13.54,1.577,438.9,13.84
ns_fix4p5_10b,10b,1328.2,867.5,589.8,3.270,180.8,0.3063,14.66,1.783,472.3,7.82
ns_fix6_10b,10b,1389.5,905.3,609.2,3.179,191.7,0.3149,14.92,1.830,499.8,3.58
ns_fix8_10b,10b,1412.5,923.7,618.9,3.230,191.7,0.3098,14.91,1.827,524.5,1.98
ns_fix11_10b,10b,1418.1,925.2,621.9,3.225,192.9,0.3102,15.06,1.845,531.7,1.59
ns_fix15_10b,10b,1422.5,927.1,621.8,3.275,189.9,0.3054,15.07,1.844,533.1,1.28
ns_fix20_10b,10b,1422.5,928.3,623.4,3.304,188.7,0.3027,15.28,1.868,529.2,1.30
ns_fix28_10b,10b,1422.4,931.5,624.1,3.318,188.1,0.3015,15.33,1.875,537.5,1.30
ns_fix4p5_10b_jump,10b,1328.2,867.5,589.8,3.270,180.8,0.3063,14.66,1.783,472.3,7.82
ns_fix8_10b_jump,10b,1412.5,923.7,618.9,3.230,191.7,0.3098,14.91,1.827,524.5,1.98
ns_fix15_10b_jump,10b,1422.5,927.1,621.8,3.275,189.9,0.3054,15.07,1.844,533.1,1.28

paired ns_fix4p5_6b vs ns_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−7.42,−7.41,−7.15,+14.63,−18.79,−12.72,−4.07,−5.14,+4.50,+28.84
p,.034,.041,.032,< .001,< .001,< .001,.021,.003,.233,.034
sig,*,*,*,***,***,***,*,**,,*

paired ns_fix6_6b vs ns_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.43,+0.52,+0.02,+14.17,−12.43,−12.58,−5.80,−5.90,+1.61,−1.74
p,.774,.751,.991,< .001,< .001,< .001,.003,.005,.489,.765
sig,,,,***,***,***,**,**,,

paired ns_fix8_6b vs ns_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+4.39,+4.37,+3.90,+17.32,−11.43,−14.89,−8.41,−9.07,+3.81,−17.15
p,.012,.020,.047,< .001,< .001,< .001,< .001,< .001,.175,.012
sig,*,*,*,***,***,***,***,***,,*

paired ns_fix11_6b vs ns_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+7.69,+7.86,+7.34,+16.75,−8.13,−14.52,−8.39,−8.97,+0.64,−30.13
p,.001,.002,.005,< .001,< .001,< .001,.003,.002,.746,.001
sig,**,**,**,***,***,***,**,**,,**

paired ns_fix15_6b vs ns_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+8.20,+8.29,+7.82,+18.64,−9.07,−15.75,−8.72,−9.67,+0.78,−32.07
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,.720,< .001
sig,***,***,***,***,***,***,***,***,,***

paired ns_fix20_6b vs ns_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+7.98,+8.45,+8.10,+18.17,−8.54,−15.51,−7.93,−9.14,+1.55,−31.13
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,.506,< .001
sig,***,***,***,***,***,***,***,***,,***

paired ns_fix28_6b vs ns_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+8.92,+9.14,+8.77,+19.20,−8.78,−16.22,−7.91,−9.10,−0.34,−34.82
p,< .001,< .001,< .001,< .001,< .001,< .001,.001,< .001,.916,< .001
sig,***,***,***,***,***,***,**,***,,***

paired ns_fix4p5_6b_jump vs ns_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−7.42,−7.41,−7.15,+14.63,−18.79,−12.72,−4.07,−5.14,+4.50,+28.84
p,.034,.041,.032,< .001,< .001,< .001,.021,.003,.233,.034
sig,*,*,*,***,***,***,*,**,,*

paired ns_fix8_6b_jump vs ns_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+4.39,+4.37,+3.90,+17.32,−11.43,−14.89,−8.41,−9.07,+3.81,−17.15
p,.012,.020,.047,< .001,< .001,< .001,< .001,< .001,.175,.012
sig,*,*,*,***,***,***,***,***,,*

paired ns_fix15_6b_jump vs ns_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+8.20,+8.29,+7.82,+18.64,−9.07,−15.75,−8.72,−9.67,+0.78,−32.07
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,.720,< .001
sig,***,***,***,***,***,***,***,***,,***

paired ns_fix4p5_10b vs ns_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−5.88,−5.60,−7.04,+1.95,−8.78,−2.00,−2.66,−1.11,+10.15,+280.61
p,.037,.042,.012,.181,.002,.167,.005,.199,.023,.037
sig,*,*,*,,**,,**,,*,*

paired ns_fix6_10b vs ns_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−1.54,−1.49,−3.99,−0.89,−3.28,+0.78,−0.90,+1.47,+16.56,+74.48
p,.027,.067,< .001,.640,.119,.680,.523,.349,< .001,.025
sig,*,,***,,,,,,***,*

paired ns_fix8_10b vs ns_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.09,+0.51,−2.46,+0.68,−3.28,−0.86,−1.01,+1.31,+22.32,−3.48
p,.699,.165,< .001,.698,.107,.622,.477,.317,< .001,.756
sig,,,***,,,,,,***,

paired ns_fix11_10b vs ns_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.49,+0.67,−1.99,+0.53,−2.67,−0.75,+0.03,+2.31,+24.00,−22.43
p,.071,.096,< .001,.800,.217,.719,.985,.154,< .001,.079
sig,,,***,,,,,,***,

paired ns_fix15_10b vs ns_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.80,+0.88,−2.00,+2.08,−4.19,−2.26,+0.08,+2.25,+24.33,−37.70
p,.009,.086,< .001,.205,.032,.167,.949,.065,< .001,.010
sig,**,,***,,*,,,,***,*

paired ns_fix20_10b vs ns_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.80,+1.01,−1.75,+3.00,−4.79,−3.13,+1.45,+3.57,+23.44,−36.70
p,.025,.064,.007,.089,.016,.073,.190,.009,< .001,.029
sig,*,,**,,*,,,**,***,*

paired ns_fix28_10b vs ns_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.79,+1.36,−1.64,+3.45,−5.10,−3.53,+1.79,+3.95,+25.36,−36.91
p,.013,.003,.002,.055,.010,.045,.130,.007,< .001,.015
sig,*,**,**,,*,*,,**,***,*

paired ns_fix4p5_10b_jump vs ns_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−5.88,−5.60,−7.04,+1.95,−8.78,−2.00,−2.66,−1.11,+10.15,+280.61
p,.037,.042,.012,.181,.002,.167,.005,.199,.023,.037
sig,*,*,*,,**,,**,,*,*

paired ns_fix8_10b_jump vs ns_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.09,+0.51,−2.46,+0.68,−3.28,−0.86,−1.01,+1.31,+22.32,−3.48
p,.699,.165,< .001,.698,.107,.622,.477,.317,< .001,.756
sig,,,***,,,,,,***,

paired ns_fix15_10b_jump vs ns_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.80,+0.88,−2.00,+2.08,−4.19,−2.26,+0.08,+2.25,+24.33,−37.70
p,.009,.086,< .001,.205,.032,.167,.949,.065,< .001,.010
sig,**,,***,,*,,,,***,*

excel cross-check: {"status": "passed", "checks": 840, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

（待填：這批數據說明了什麼、可信度、下一步）
