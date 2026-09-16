# 2026-09-16_large_packing_capacity_m5

- 類別：正式實驗（10 seeds）
- 產生：2026-09-16T09:11:03・正典 v1・DLL 94a44bdb4844・git retro
- 場次狀態：valid=40

## 為何做這組實驗

**問題**：大規模 45 bots、12 揀貨站下，配置 1/2/3 個包裝站（每站 78 箱）相對無限制時拆單效益流失多少？需要幾個包裝站才保住效益？

**動機**：使用者要求的延伸實驗；包裝站數為整數。Canon v1 不設限時同時等待合併的母單峰值 175–201（中位 190）、時間平均 137，故 1 站（78）預期明顯受限、2 站（156）接近時間平均、3 站（234）高於峰值。對照組沿用既有 m5_45b。

## 設定

- **m5_45b**：hgs_m5_k10.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
- **m5_pk1_45b**：hgs_m5_k10_pk1.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <PackingStationCount>1</PackingStationCount>`；`+    <PackingBufferCapacity>78</PackingBufferCapacity>`
- **m5_pk2_45b**：hgs_m5_k10_pk2.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <PackingStationCount>2</PackingStationCount>`；`+    <PackingBufferCapacity>78</PackingBufferCapacity>`
- **m5_pk3_45b**：hgs_m5_k10_pk3.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <PackingStationCount>3</PackingStationCount>`；`+    <PackingBufferCapacity>78</PackingBufferCapacity>`

比較：
- m5_pk1_45b vs m5_45b（ablation；變數 PackingStationCount=1 (78 boxes each)）
- m5_pk2_45b vs m5_45b（ablation；變數 PackingStationCount=2 (78 boxes each)）
- m5_pk3_45b vs m5_45b（ablation；變數 PackingStationCount=3 (78 boxes each)）

## 結果

```
2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_45b,45b,8318.2,5396.9,3385.3,7.625,444.1,0.1312,20.45,3.601,395.2,3.79
m5_pk1_45b,45b,2079.2,1358.0,885.1,5.002,177.4,0.2003,46.84,8.013,651.8,75.94
m5_pk2_45b,45b,5371.3,3494.6,2198.8,7.006,313.4,0.1428,25.15,4.437,497.6,37.86
m5_pk3_45b,45b,8313.4,5394.8,3383.5,7.628,443.7,0.1311,20.46,3.601,395.4,3.84

paired m5_pk1_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−75.00,−74.84,−73.85,−34.40,−60.05,+52.71,+129.08,+122.53,+64.93,+1904.75
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,***,***,***,***,***,***,***,***

paired m5_pk2_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−35.43,−35.25,−35.05,−8.11,−29.43,+8.89,+23.01,+23.22,+25.92,+899.51
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,***,***,***,***,***,***,***,***

paired m5_pk3_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.06,−0.04,−0.05,+0.04,−0.09,−0.04,+0.06,+0.01,+0.05,+1.43
p,.343,.343,.343,.343,.343,.343,.343,.343,.343,.343
sig,,,,,,,,,,

excel cross-check: {"status": "passed", "checks": 140, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

- 結論：3 個包裝站（234 箱）與無限制在全部 10 項指標上無顯著差異（p=.343，僅 1 seed 微差）；2 站件數 −35.4%***、1 站 −75.0%***（站台閒置 3.8%→75.9%）。拐點落在 2～3 站之間，與不設限時同時等待合併母單峰值 175–201（中位 190）一致。
- 比例：12 個揀貨站需 3 個 78 箱包裝站（4:1），或每揀貨站約 17 箱合併緩衝。
- ⚠️ 1 站崩潰到 2079 件遠低於 HADGS（6341）不是拆單的代價，是行級貪婪的表達力缺口：箱子用完後只允許「只剩一行」的訂單移動，多行訂單全部凍結。M4G 的 MILP 會自然退化成整單派車，M5 需要明寫。已實作 PackingFullWholeOrderFallback（預設 false），守門員與測試進行中；若通過並烘入正典，1、2 站需重跑，本實驗會被 stale-check 標為 superseded。
- 3 站與無限制的結論不受此影響（容量從未觸頂）。
- 可信度：10 seeds、40/40 valid、Canon v1、對照沿用 canon_v1_large_split_limit_m5 的 m5_45b、Excel 通過。
