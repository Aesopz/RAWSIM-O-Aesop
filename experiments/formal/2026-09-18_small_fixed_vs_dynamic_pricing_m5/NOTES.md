# 2026-09-18_small_fixed_vs_dynamic_pricing_m5

- 類別：正式實驗（10 seeds）
- 產生：2026-09-18T01:05:05・正典 v2・DLL 94a44bdb4844・git retro
- 場次狀態：superseded=20, valid=140

## 為何做這組實驗

**問題**：小規模 HGS-M5 下，Dinkelbach 動態定價是否優於事後最佳的固定 λ/μ？（與 M4G 同設計，分離『求解器』與『規模』）

**動機**：現有小規模固定 vs 動態只在 M4G（精確解）做過；大規模只在 M5。無法分辨動態在大規模失去優勢是規模還是貪婪求解器所致。格點錨定 M5 小規模動態 λ_end 分位（P10/25/50/75/90/95 ≈ 3/3.9/4.8/14/18.5/22），μ 依 M5 動態 μ/λ 中位（6b 1.6222、10b 1.6183）。動態組沿用 2026-09-15_canon_v1_small_split_limit_fidelity 的 m5_6b/m5_10b（guard 2026-09-17 證 v2 逐位相同）。

## 設定

- **m5_dyn_6b**：hgs_m5.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m5_fix3_6b**：m5_fix3_6b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>3</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>4.8666</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix4_6b**：m5_fix4_6b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>4</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>6.4888</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix5_6b**：m5_fix5_6b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>5</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>8.111</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix9_6b**：m5_fix9_6b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>9</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>14.5998</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix14_6b**：m5_fix14_6b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>14</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>22.7108</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix18_6b**：m5_fix18_6b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>18</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>29.1996</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix22_6b**：m5_fix22_6b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>22</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>35.6884</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_dyn_10b**：hgs_m5.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m5_fix3_10b**：m5_fix3_10b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>3</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>4.8549</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix4_10b**：m5_fix4_10b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>4</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>6.4732</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix5_10b**：m5_fix5_10b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>5</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>8.0915</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix9_10b**：m5_fix9_10b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>9</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>14.5647</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix14_10b**：m5_fix14_10b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>14</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>22.6562</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix18_10b**：m5_fix18_10b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>18</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>29.1294</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_fix22_10b**：m5_fix22_10b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>22</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>35.6026</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`

比較：
- m5_fix3_6b vs m5_dyn_6b（ablation；變數 fixed lambda=3, mu=4.8666, no lambda iteration, no bound jump）
- m5_fix4_6b vs m5_dyn_6b（ablation；變數 fixed lambda=4, mu=6.4888, no lambda iteration, no bound jump）
- m5_fix5_6b vs m5_dyn_6b（ablation；變數 fixed lambda=5, mu=8.111, no lambda iteration, no bound jump）
- m5_fix9_6b vs m5_dyn_6b（ablation；變數 fixed lambda=9, mu=14.5998, no lambda iteration, no bound jump）
- m5_fix14_6b vs m5_dyn_6b（ablation；變數 fixed lambda=14, mu=22.7108, no lambda iteration, no bound jump）
- m5_fix18_6b vs m5_dyn_6b（ablation；變數 fixed lambda=18, mu=29.1996, no lambda iteration, no bound jump）
- m5_fix22_6b vs m5_dyn_6b（ablation；變數 fixed lambda=22, mu=35.6884, no lambda iteration, no bound jump）
- m5_fix3_10b vs m5_dyn_10b（ablation；變數 fixed lambda=3, mu=4.8549, no lambda iteration, no bound jump）
- m5_fix4_10b vs m5_dyn_10b（ablation；變數 fixed lambda=4, mu=6.4732, no lambda iteration, no bound jump）
- m5_fix5_10b vs m5_dyn_10b（ablation；變數 fixed lambda=5, mu=8.0915, no lambda iteration, no bound jump）
- m5_fix9_10b vs m5_dyn_10b（ablation；變數 fixed lambda=9, mu=14.5647, no lambda iteration, no bound jump）
- m5_fix14_10b vs m5_dyn_10b（ablation；變數 fixed lambda=14, mu=22.6562, no lambda iteration, no bound jump）
- m5_fix18_10b vs m5_dyn_10b（ablation；變數 fixed lambda=18, mu=29.1294, no lambda iteration, no bound jump）
- m5_fix22_10b vs m5_dyn_10b（ablation；變數 fixed lambda=22, mu=35.6026, no lambda iteration, no bound jump）

## 結果

```
WARNING: superseded (old canon) data in arms: m5_dyn_10b, m5_dyn_6b
2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_dyn_6b,6b,1422.2,931.2,584.0,5.231,111.7,0.1913,9.40,1.203,916.9,1.30
m5_fix3_6b,6b,882.8,586.7,364.6,5.562,65.7,0.1801,9.90,1.285,712.7,38.69
m5_fix4_6b,6b,1136.2,747.3,475.7,5.426,89.0,0.1850,9.27,1.173,775.6,21.13
m5_fix5_6b,6b,1215.9,798.6,503.4,5.476,92.5,0.1830,8.98,1.149,787.2,15.61
m5_fix9_6b,6b,1422.2,932.2,589.8,5.768,102.3,0.1735,9.06,1.145,866.7,1.31
m5_fix14_6b,6b,1424.5,933.7,587.7,5.748,102.3,0.1741,9.13,1.156,879.8,1.14
m5_fix18_6b,6b,1422.5,931.1,588.7,5.741,102.6,0.1744,9.26,1.166,886.2,1.29
m5_fix22_6b,6b,1424.3,933.4,588.7,5.856,100.6,0.1709,9.17,1.159,913.8,1.16
m5_dyn_10b,10b,1430.3,934.2,588.1,5.081,115.8,0.1970,10.18,1.312,955.2,0.74
m5_fix3_10b,10b,936.3,616.5,387.6,5.482,71.4,0.1828,10.22,1.305,749.6,34.99
m5_fix4_10b,10b,993.2,653.9,413.3,5.516,75.7,0.1817,9.69,1.228,735.8,31.05
m5_fix5_10b,10b,1197.0,783.3,496.2,5.554,89.4,0.1801,9.75,1.241,803.4,16.90
m5_fix9_10b,10b,1411.0,925.5,585.6,5.655,103.6,0.1770,9.82,1.238,856.9,2.08
m5_fix14_10b,10b,1428.6,934.0,591.0,5.561,106.3,0.1799,9.99,1.259,915.4,0.84
m5_fix18_10b,10b,1428.4,933.8,591.4,5.639,105.0,0.1776,9.98,1.260,877.7,0.88
m5_fix22_10b,10b,1428.4,933.7,591.9,5.539,107.0,0.1807,10.14,1.275,902.5,0.88

paired m5_fix3_6b vs m5_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−37.93,−37.00,−37.57,+6.34,−41.18,−5.83,+5.27,+6.82,−22.27,+2880.03
p,< .001,< .001,< .001,.005,< .001,.005,.183,.067,< .001,< .001
sig,***,***,***,**,***,**,,,***,***

paired m5_fix4_6b vs m5_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−20.11,−19.75,−18.54,+3.73,−20.32,−3.27,−1.41,−2.48,−15.41,+1527.56
p,.014,.014,.028,.105,.020,.092,.660,.354,.025,.014
sig,*,*,*,,*,,,,*,*

paired m5_fix5_6b vs m5_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−14.51,−14.24,−13.80,+4.68,−17.19,−4.33,−4.50,−4.52,−14.14,+1101.98
p,.058,.061,.074,.015,.029,.015,.038,.007,.045,.058
sig,,,,*,*,*,*,**,*,

paired m5_fix9_6b vs m5_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,0.00,+0.11,+0.99,+10.27,−8.42,−9.29,−3.69,−4.86,−5.47,+0.62
p,1.000,.657,.012,< .001,< .001,< .001,< .001,< .001,.015,.926
sig,,,*,***,***,***,***,***,*,

paired m5_fix14_6b vs m5_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.16,+0.27,+0.63,+9.88,−8.42,−8.96,−2.86,−3.95,−4.04,−12.00
p,.165,.236,.016,< .001,< .001,< .001,.008,.001,.090,.153
sig,,,*,***,***,***,**,**,,

paired m5_fix18_6b vs m5_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.02,−0.01,+0.80,+9.76,−8.15,−8.84,−1.48,−3.05,−3.34,−0.50
p,.892,.971,.036,< .001,< .001,< .001,.144,< .001,.283,.964
sig,,,*,***,***,***,,***,,

paired m5_fix22_6b vs m5_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.15,+0.24,+0.80,+11.95,−9.94,−10.63,−2.44,−3.68,−0.33,−10.92
p,.368,.283,.059,< .001,< .001,< .001,.027,< .001,.876,.363
sig,,,,***,***,***,*,***,,

paired m5_fix3_10b vs m5_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−34.54,−34.01,−34.09,+7.89,−38.34,−7.18,+0.34,−0.50,−21.52,+4658.88
p,.001,.001,.002,.001,< .001,< .001,.926,.888,.004,.001
sig,**,**,**,**,***,***,,,**,**

paired m5_fix4_10b vs m5_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−30.56,−30.00,−29.72,+8.57,−34.63,−7.76,−4.84,−6.40,−22.97,+4122.63
p,.003,.003,.004,.003,.002,.002,.177,.055,< .001,.003
sig,**,**,**,**,**,**,,,***,**

paired m5_fix5_10b vs m5_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−16.31,−16.15,−15.63,+9.31,−22.80,−8.54,−4.26,−5.42,−15.89,+2198.23
p,.048,.050,.058,< .001,.006,< .001,.090,.027,.025,.048
sig,*,*,,***,**,***,,*,*,*

paired m5_fix9_10b vs m5_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−1.35,−0.93,−0.43,+11.31,−10.54,−10.13,−3.55,−5.62,−10.29,+182.79
p,.249,.451,.744,< .001,< .001,< .001,.003,< .001,< .001,.244
sig,,,,***,***,***,**,***,***,

paired m5_fix14_10b vs m5_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.12,−0.02,+0.49,+9.46,−8.20,−8.67,−1.94,−4.07,−4.16,+14.71
p,< .001,.936,.372,< .001,< .001,< .001,.066,< .001,.217,< .001
sig,***,,,***,***,***,,***,,***

paired m5_fix18_10b vs m5_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.13,−0.04,+0.56,+11.00,−9.33,−9.82,−1.97,−3.99,−8.11,+19.40
p,.003,.737,.146,< .001,< .001,< .001,.048,.002,.005,< .001
sig,**,,,***,***,***,*,**,**,***

paired m5_fix22_10b vs m5_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.13,−0.05,+0.65,+9.01,−7.60,−8.25,−0.44,−2.84,−5.51,+19.46
p,< .001,.544,.202,< .001,< .001,< .001,.674,.020,.039,< .001
sig,***,,,***,***,***,,*,*,***

excel cross-check: {"status": "passed", "checks": 600, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

**結論**：小規模 HGS-M5 下動態定價**不優於**固定高 λ（9–22）：件數持平（6b 0～+0.2、10b −0.1%），m/line 輸 0.4～3.7%、EOR 輸 2.8～5.6%、trips 多 8～10%、pile-on 低 9～12%（多數 p<.01）。低 λ（3–5）在 M5 一樣崩潰（−15～−38%）。
**與 M4G 同設計對照**：M4G 動態對固定高 λ 距離贏 15～44%；M5 反過來輸 1～4%。同佈局同 seed 只換求解器，方向反轉 ⇒ 「動態 > 固定」是 M4G（精確解）的性質，M5 未繼承。
**機制**：M4G 固定高 λ 過度派車（10b trips +60%）——估值層許諾線在高價下全值錢，MILP 大量派車兌現；M5 dispatch 為邊際試算，高 λ 不濫派（固定 λ≥9 trips 比動態少 8～10%），動態 λ*≈4.8＋λ̄ 跳躍反成多派方。與大規模結論一致：是求解器不是規模。
**敘事影響**：定價優於常數的主張限定 M4G；M5 的動態 λ 提供免調參與不崩潰，不提供效率優勢。「小規模證明、大規模邊界」不成立。
可信度：10 seeds、雙尾配對 t、excel 交叉驗證通過；動態組沿用 guard 證明逐位相同的 v1 run。
