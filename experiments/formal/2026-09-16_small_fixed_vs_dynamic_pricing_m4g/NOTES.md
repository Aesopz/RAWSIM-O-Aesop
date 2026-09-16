# 2026-09-16_small_fixed_vs_dynamic_pricing_m4g

- 類別：正式實驗（10 seeds）
- 產生：2026-09-16T19:47:17・正典 v1・DLL 94a44bdb4844・git retro
- 場次狀態：valid=160

## 為何做這組實驗

**問題**：Canon v1 M4G（拆單）下，Dinkelbach 動態定價是否優於事後最佳的固定 λ/μ？

**動機**：取代第 24/27 頁舊正典資料；昨日誤用 M4G-NS（不拆單，定價無用武之地）。格點錨定 M4G 動態派車 λ 分位（6b P10/P50/P90=2.53/4.29/22.5；10b 2.63/4.12/20.5）；μ 依 M4G 動態 μ/λ 中位（6b 0.7879、10b 0.8339）同步固定；β 不固定；DinkelbachIterations=0 使跳躍不觸發，故不另設跳躍組。動態組沿用 Canon v1 M4G。

## 設定

- **m4g_dyn_6b**：m4g.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m4g_fix4p5_6b**：m4g_fix4p5_6b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>4.5</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>3.5456</MuFixed>`
- **m4g_fix6_6b**：m4g_fix6_6b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>6</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>4.7274</MuFixed>`
- **m4g_fix8_6b**：m4g_fix8_6b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>8</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>6.3032</MuFixed>`
- **m4g_fix11_6b**：m4g_fix11_6b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>11</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>8.6669</MuFixed>`
- **m4g_fix15_6b**：m4g_fix15_6b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>15</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>11.8185</MuFixed>`
- **m4g_fix20_6b**：m4g_fix20_6b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>20</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>15.758</MuFixed>`
- **m4g_fix28_6b**：m4g_fix28_6b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>28</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>22.0612</MuFixed>`
- **m4g_dyn_10b**：m4g.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m4g_fix4p5_10b**：m4g_fix4p5_10b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>4.5</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>3.7525</MuFixed>`
- **m4g_fix6_10b**：m4g_fix6_10b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>6</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>5.0034</MuFixed>`
- **m4g_fix8_10b**：m4g_fix8_10b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>8</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>6.6712</MuFixed>`
- **m4g_fix11_10b**：m4g_fix11_10b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>11</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>9.1729</MuFixed>`
- **m4g_fix15_10b**：m4g_fix15_10b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>15</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>12.5085</MuFixed>`
- **m4g_fix20_10b**：m4g_fix20_10b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>20</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>16.678</MuFixed>`
- **m4g_fix28_10b**：m4g_fix28_10b.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>28</LambdaFixed>`；`-    <DinkelbachIterations>5</DinkelbachIterations>`；`+    <DinkelbachIterations>0</DinkelbachIterations>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MuFixed>23.3492</MuFixed>`

比較：
- m4g_fix4p5_6b vs m4g_dyn_6b（ablation；變數 fixed lambda=4.5, mu=3.5456, no Dinkelbach, no bound jump）
- m4g_fix6_6b vs m4g_dyn_6b（ablation；變數 fixed lambda=6, mu=4.7274, no Dinkelbach, no bound jump）
- m4g_fix8_6b vs m4g_dyn_6b（ablation；變數 fixed lambda=8, mu=6.3032, no Dinkelbach, no bound jump）
- m4g_fix11_6b vs m4g_dyn_6b（ablation；變數 fixed lambda=11, mu=8.6669, no Dinkelbach, no bound jump）
- m4g_fix15_6b vs m4g_dyn_6b（ablation；變數 fixed lambda=15, mu=11.8185, no Dinkelbach, no bound jump）
- m4g_fix20_6b vs m4g_dyn_6b（ablation；變數 fixed lambda=20, mu=15.758, no Dinkelbach, no bound jump）
- m4g_fix28_6b vs m4g_dyn_6b（ablation；變數 fixed lambda=28, mu=22.0612, no Dinkelbach, no bound jump）
- m4g_fix4p5_10b vs m4g_dyn_10b（ablation；變數 fixed lambda=4.5, mu=3.7525, no Dinkelbach, no bound jump）
- m4g_fix6_10b vs m4g_dyn_10b（ablation；變數 fixed lambda=6, mu=5.0034, no Dinkelbach, no bound jump）
- m4g_fix8_10b vs m4g_dyn_10b（ablation；變數 fixed lambda=8, mu=6.6712, no Dinkelbach, no bound jump）
- m4g_fix11_10b vs m4g_dyn_10b（ablation；變數 fixed lambda=11, mu=9.1729, no Dinkelbach, no bound jump）
- m4g_fix15_10b vs m4g_dyn_10b（ablation；變數 fixed lambda=15, mu=12.5085, no Dinkelbach, no bound jump）
- m4g_fix20_10b vs m4g_dyn_10b（ablation；變數 fixed lambda=20, mu=16.678, no Dinkelbach, no bound jump）
- m4g_fix28_10b vs m4g_dyn_10b（ablation；變數 fixed lambda=28, mu=23.3492, no Dinkelbach, no bound jump）

## 結果

```
2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m4g_dyn_6b,6b,1419.9,946.1,599.1,5.338,112.3,0.1875,9.37,1.190,830.8,1.45
m4g_fix4p5_6b,6b,946.3,634.3,408.5,4.634,89.9,0.2166,10.90,1.374,587.6,34.30
m4g_fix6_6b,6b,1264.3,837.2,540.0,4.262,127.1,0.2352,10.96,1.378,610.2,12.25
m4g_fix8_6b,6b,1264.1,841.9,541.2,4.247,129.3,0.2364,10.85,1.369,596.2,12.29
m4g_fix11_6b,6b,1391.8,923.0,590.8,4.404,134.2,0.2273,10.85,1.378,644.4,3.42
m4g_fix15_6b,6b,1390.4,924.8,588.8,4.513,130.5,0.2217,10.83,1.378,680.3,3.50
m4g_fix20_6b,6b,1394.1,926.8,590.4,4.553,129.7,0.2197,10.79,1.372,677.1,3.25
m4g_fix28_6b,6b,1396.2,928.9,593.1,4.582,129.5,0.2184,10.93,1.385,665.1,3.09
m4g_dyn_10b,10b,1428.7,952.2,603.5,5.118,118.0,0.1956,10.35,1.317,787.8,0.86
m4g_fix4p5_10b,10b,937.1,633.5,405.5,4.539,94.2,0.2232,10.68,1.362,535.0,34.95
m4g_fix6_10b,10b,1309.0,859.5,557.0,3.491,159.9,0.2872,13.34,1.695,572.5,9.15
m4g_fix8_10b,10b,1402.9,923.2,589.5,3.233,182.5,0.3097,14.18,1.830,569.5,2.65
m4g_fix11_10b,10b,1426.0,941.1,596.0,3.102,192.2,0.3225,14.34,1.879,618.6,1.04
m4g_fix15_10b,10b,1425.2,939.6,595.0,3.093,192.4,0.3234,14.58,1.903,616.8,1.09
m4g_fix20_10b,10b,1425.4,939.5,593.7,3.145,188.8,0.3180,14.60,1.911,637.3,1.09
m4g_fix28_10b,10b,1424.6,937.5,592.1,3.154,187.8,0.3172,14.94,1.954,614.2,1.15

paired m4g_fix4p5_6b vs m4g_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−33.35,−32.96,−31.81,−13.18,−19.95,+15.49,+16.32,+15.53,−29.27,+2260.43
p,.003,.004,.005,< .001,.092,< .001,.007,.007,< .001,.003
sig,**,**,**,***,,***,**,**,***,**

paired m4g_fix6_6b vs m4g_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−10.96,−11.51,−9.86,−20.16,+13.18,+25.41,+16.97,+15.82,−26.55,+742.88
p,.004,.002,.008,< .001,.011,< .001,< .001,< .001,< .001,.004
sig,**,**,**,***,*,***,***,***,***,**

paired m4g_fix8_6b vs m4g_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−10.97,−11.01,−9.66,−20.44,+15.14,+26.07,+15.73,+15.11,−28.24,+746.09
p,.152,.155,.208,< .001,.138,< .001,< .001,< .001,< .001,.151
sig,,,,***,,***,***,***,***,

paired m4g_fix11_6b vs m4g_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−1.98,−2.44,−1.39,−17.50,+19.50,+21.20,+15.73,+15.82,−22.43,+135.56
p,< .001,.002,.066,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,**,,***,***,***,***,***,***,***

paired m4g_fix15_6b vs m4g_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−2.08,−2.25,−1.72,−15.46,+16.21,+18.22,+15.51,+15.83,−18.11,+141.20
p,< .001,< .001,.026,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,*,***,***,***,***,***,***,***

paired m4g_fix20_6b vs m4g_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−1.82,−2.04,−1.45,−14.70,+15.49,+17.17,+15.07,+15.34,−18.50,+123.52
p,< .001,< .001,.070,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,,***,***,***,***,***,***,***

paired m4g_fix28_6b vs m4g_dyn_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−1.67,−1.82,−1.00,−14.16,+15.32,+16.45,+16.59,+16.47,−19.94,+112.60
p,< .001,< .001,.071,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,,***,***,***,***,***,***,***

paired m4g_fix4p5_10b vs m4g_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−34.41,−33.47,−32.81,−11.30,−20.17,+14.12,+3.22,+3.40,−32.08,+3984.10
p,.004,.005,.006,.016,.156,.014,.561,.489,< .001,.004
sig,**,**,**,*,,*,,,***,**

paired m4g_fix6_10b vs m4g_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−8.38,−9.74,−7.71,−31.77,+35.51,+46.87,+28.89,+28.68,−27.33,+968.78
p,.015,.004,.024,< .001,< .001,< .001,< .001,< .001,< .001,.015
sig,*,**,*,***,***,***,***,***,***,*

paired m4g_fix8_10b vs m4g_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−1.81,−3.05,−2.32,−36.83,+54.66,+58.38,+36.97,+38.96,−27.71,+209.32
p,< .001,< .001,.003,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,**,***,***,***,***,***,***,***

paired m4g_fix11_10b vs m4g_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.19,−1.17,−1.24,−39.39,+62.88,+64.93,+38.52,+42.68,−21.47,+21.27
p,.059,.020,.013,< .001,< .001,< .001,< .001,< .001,< .001,.056
sig,,*,*,***,***,***,***,***,***,

paired m4g_fix15_10b vs m4g_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.24,−1.32,−1.41,−39.56,+63.05,+65.35,+40.86,+44.48,−21.70,+27.88
p,.085,.005,.007,< .001,< .001,< .001,< .001,< .001,< .001,.087
sig,,**,**,***,***,***,***,***,***,

paired m4g_fix20_10b vs m4g_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.23,−1.33,−1.62,−38.54,+60.00,+62.61,+41.01,+45.06,−19.11,+26.83
p,.060,.002,< .001,< .001,< .001,< .001,< .001,< .001,< .001,.050
sig,,**,***,***,***,***,***,***,***,

paired m4g_fix28_10b vs m4g_dyn_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.29,−1.54,−1.89,−38.37,+59.15,+62.20,+44.36,+48.36,−22.03,+34.00
p,.003,.007,.005,< .001,< .001,< .001,< .001,< .001,< .001,.004
sig,**,**,**,***,***,***,***,***,***,**

excel cross-check: {"status": "passed", "checks": 600, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

- 結論：在 M4G（拆單）下，動態 Dinkelbach 定價在所有 7 個固定 λ 上勝出；即使讓固定組事後挑最佳值，動態仍在 EOR 上領先 13%（6b，vs λ=8）～28%（10b，vs λ=8）***、pile-on 領先 15～40%***，件數持平（10b n.s.）或略勝（6b +1.7%***）。
- 兩端行為：λ=4.5（≈動態中位數）件數 −33%***、站台閒置 +33 pp（常數低於當期 λ* 時空任務）；λ≥11 件數追平但趟次 +55%～+63%（接受低效趟次）。沒有常數能同時避開兩端。
- 代價：固定組 Turnover 全部較短（−18%～−32%），需揭露。
- 為何 NS 版失敗：NS 目標式只有整單獎勵，λ 無作用面；定價的價值需要行級拆單才顯現（與 A/B 一致）。
- 圖：figures/fig1_distance_vs_items（動態在右下角，所有固定在左上）、fig2_delta_by_measure。
- 可信度：10 seeds、160/160 valid、Canon v1（M4G 不受 v2 影響）、Excel 通過。用於第 24/27 頁替換舊正典資料。
