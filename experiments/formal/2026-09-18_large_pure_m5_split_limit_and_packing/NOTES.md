# 2026-09-18_large_pure_m5_split_limit_and_packing

- 類別：正式實驗（10 seeds）
- 產生：2026-09-18T04:10:48・正典 v2・DLL 2b11c291972a・git c4f718664f
- 場次狀態：valid=70

## 為何做這組實驗

**問題**：純貪婪 HGS-M5 下，拆分份數 N∈{2,3,4} 與包裝站數 {1,2,3} 對吞吐與效率的影響（鏡像 C9／C10，使主線與附錄同一政策）

**動機**：主線改為純貪婪 HGS-M5 後，C9（包裝站 1/2/3）與 C10（N=2/3/4）仍為 HGS-M5-λ 資料，政策不一致。以純貪婪重跑兩組掃描；無上限基準沿用 2026-09-18_large_m5_pure_vs_lambda_vs_hadgs/m5_pure_45b。預算綁定時 Canon v2 整單 fallback 會啟動（純貪婪版保留）。

## 設定

- **m5_pure_45b**：m5_pure_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_pure_n2_45b**：m5_pure_n2_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MaxPartsPerOrder>2</MaxPartsPerOrder>`
- **m5_pure_n3_45b**：m5_pure_n3_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MaxPartsPerOrder>3</MaxPartsPerOrder>`
- **m5_pure_n4_45b**：m5_pure_n4_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <MaxPartsPerOrder>4</MaxPartsPerOrder>`
- **m5_pure_pk1_45b**：m5_pure_pk1_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <PackingStationCount>1</PackingStationCount>`；`+    <PackingBufferCapacity>78</PackingBufferCapacity>`
- **m5_pure_pk2_45b**：m5_pure_pk2_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <PackingStationCount>2</PackingStationCount>`；`+    <PackingBufferCapacity>78</PackingBufferCapacity>`
- **m5_pure_pk3_45b**：m5_pure_pk3_45b.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`；`+    <PackingStationCount>3</PackingStationCount>`；`+    <PackingBufferCapacity>78</PackingBufferCapacity>`

比較：
- m5_pure_n2_45b vs m5_pure_45b（sensitivity；變數 MaxPartsPerOrder=2）
- m5_pure_n3_45b vs m5_pure_45b（sensitivity；變數 MaxPartsPerOrder=3）
- m5_pure_n4_45b vs m5_pure_45b（sensitivity；變數 MaxPartsPerOrder=4）
- m5_pure_pk1_45b vs m5_pure_45b（sensitivity；變數 PackingStationCount=1, capacity 78）
- m5_pure_pk2_45b vs m5_pure_45b（sensitivity；變數 PackingStationCount=2, capacity 78）
- m5_pure_pk3_45b vs m5_pure_45b（sensitivity；變數 PackingStationCount=3, capacity 78）

## 結果

```
2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_pure_45b,45b,8151.3,5289.6,3323.4,8.362,397.5,0.1196,21.57,3.938,399.0,5.71
m5_pure_n2_45b,45b,7973.1,5160.4,3347.9,5.151,651.0,0.1945,27.44,4.815,314.6,7.78
m5_pure_n3_45b,45b,8155.5,5282.7,3323.1,7.696,432.2,0.1301,22.14,4.017,402.3,5.67
m5_pure_n4_45b,45b,8154.7,5285.8,3324.1,8.360,397.7,0.1196,21.58,3.933,399.5,5.68
m5_pure_pk1_45b,45b,8026.5,5204.3,3367.5,4.193,804.6,0.2390,31.21,5.749,282.7,7.16
m5_pure_pk2_45b,45b,8132.9,5276.1,3350.4,7.248,462.9,0.1382,23.22,4.204,377.9,5.93
m5_pure_pk3_45b,45b,8151.3,5289.6,3323.4,8.362,397.5,0.1196,21.57,3.938,399.0,5.71

paired m5_pure_n2_45b vs m5_pure_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−2.19,−2.44,+0.74,−38.40,+63.77,+62.60,+27.22,+22.27,−21.16,+36.21
p,< .001,< .001,.068,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,,***,***,***,***,***,***,***

paired m5_pure_n3_45b vs m5_pure_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.05,−0.13,−0.01,−7.97,+8.73,+8.74,+2.64,+2.02,+0.83,−0.68
p,.797,.479,.968,< .001,< .001,< .001,< .001,.004,.285,.836
sig,,,,***,***,***,***,**,,

paired m5_pure_n4_45b vs m5_pure_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.04,−0.07,+0.02,−0.02,+0.05,+0.02,+0.05,−0.12,+0.10,−0.61
p,.336,.488,.871,.929,.849,.935,.882,.472,.743,.432
sig,,,,,,,,,,

paired m5_pure_pk1_45b vs m5_pure_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−1.53,−1.61,+1.33,−49.86,+102.42,+99.82,+44.66,+46.00,−29.17,+25.33
p,< .001,< .001,.002,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,**,***,***,***,***,***,***,***

paired m5_pure_pk2_45b vs m5_pure_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.23,−0.26,+0.81,−13.33,+16.45,+15.53,+7.64,+6.76,−5.29,+3.84
p,.248,.360,.009,< .001,< .001,< .001,< .001,< .001,< .001,.234
sig,,,**,***,***,***,***,***,***,

paired m5_pure_pk3_45b vs m5_pure_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00
p,identical,identical,identical,identical,identical,identical,identical,identical,identical,identical
sig,identical,identical,identical,identical,identical,identical,identical,identical,identical,identical

excel cross-check: {"status": "passed", "checks": 250, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

**結論**：純貪婪 HGS-M5 的吞吐對拆分預算幾乎不敏感（N=2 −2.2%***、1 站 −1.5%***、其餘 n.s.），代價全在效率：N=2 m/line +27%***／EOR +22%***；1 站 m/line +45%***／EOR +46%***；2 站 +7.6%／+6.8%***；N=3 +2.6%／+2.0%。N≥4 與 3 站與無上限逐位相同（預算從不綁定）。Turnover 反向 −21～−29%（fallback 整單派完不等合併）。
**與 HGS-M5-λ v2 對照**：方向相同；純貪婪吞吐損失更小（λ 版 1 站 −6.3%），效率損失相近。主線與附錄現在同一政策。
可信度：10 seeds、雙尾配對 t、excel 交叉驗證通過；基準沿用 2026-09-18_large_m5_pure_vs_lambda_vs_hadgs/m5_pure_45b。
圖：figures/fig_n_sensitivity.png、fig_pk_sensitivity.png（F1–F11，星號＝vs 無上限）。
