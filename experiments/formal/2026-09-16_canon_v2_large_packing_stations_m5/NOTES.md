# 2026-09-16_canon_v2_large_packing_stations_m5

- 類別：正式實驗（10 seeds）
- 產生：2026-09-16T19:52:29・正典 v2・DLL 94a44bdb4844・git retro
- 場次狀態：valid=40

## 為何做這組實驗

**問題**：Canon v2 下，12 揀貨站配 1/2/3 個包裝站（每站 78 箱）相對無限制的效益流失？需要幾個包裝站？

**動機**：Canon v1 的 1、2 站結果被凍結汙染（1 站 seed0 1985 → v2 7815）；3 站與無限制未觸頂，沿用 v1（逐位不變）。

## 設定

- **m5_45b**：hgs_m5_k10.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
- **m5_pk3_45b**：hgs_m5_k10_pk3.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <PackingStationCount>3</PackingStationCount>`；`+    <PackingBufferCapacity>78</PackingBufferCapacity>`
- **m5_pk1_45b**：hgs_m5_k10_pk1.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <PackingStationCount>1</PackingStationCount>`；`+    <PackingBufferCapacity>78</PackingBufferCapacity>`
- **m5_pk2_45b**：hgs_m5_k10_pk2.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <PackingStationCount>2</PackingStationCount>`；`+    <PackingBufferCapacity>78</PackingBufferCapacity>`

比較：
- m5_pk1_45b vs m5_45b（ablation；變數 PackingStationCount=1 (78 boxes each)）
- m5_pk2_45b vs m5_45b（ablation；變數 PackingStationCount=2 (78 boxes each)）
- m5_pk3_45b vs m5_45b（ablation；變數 PackingStationCount=3 (78 boxes each)）

## 結果

```
2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_45b,45b,8318.2,5396.9,3385.3,7.625,444.1,0.1312,20.45,3.601,395.2,3.79
m5_pk3_45b,45b,8313.4,5394.8,3383.5,7.628,443.7,0.1311,20.46,3.601,395.4,3.84
m5_pk1_45b,45b,7797.2,5061.5,3257.0,3.344,974.8,0.2993,30.12,5.510,301.3,9.81
m5_pk2_45b,45b,8312.2,5393.3,3425.1,6.191,553.5,0.1616,22.77,3.963,372.0,3.86

paired m5_pk1_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−6.26,−6.21,−3.79,−56.15,+119.50,+128.19,+47.31,+53.01,−23.76,+158.98
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,***,***,***,***,***,***,***,***

paired m5_pk2_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.07,−0.07,+1.18,−18.80,+24.63,+23.19,+11.36,+10.07,−5.87,+1.92
p,.585,.718,.003,< .001,< .001,< .001,< .001,< .001,< .001,.577
sig,,,**,***,***,***,***,***,***,

paired m5_pk3_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.06,−0.04,−0.05,+0.04,−0.09,−0.04,+0.06,+0.01,+0.05,+1.43
p,.343,.343,.343,.343,.343,.343,.343,.343,.343,.343
sig,,,,,,,,,,

excel cross-check: {"status": "passed", "checks": 140, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

- 結論（Canon v2）：合併容量幾乎不影響吞吐——2 站（156 箱）件數與無限制 n.s.（−0.07%），1 站（78 箱）只 −6.3%***；效率隨容量遞減：1 站 EOR +53%***、2 站 +10%***、3 站 0（逐位同無限制）。
- 設計比例（12 揀貨站）：2 個包裝站保住吞吐與 90% 的能耗效益；3 個拿滿。約 6:1～4:1。
- 對 HADGS（6341 件、EOR 7.74）：即使只有 1 個包裝站，M5 仍 +23% 件數、−29% 能耗 ⟹ 拆單在合併容量極度受限時仍值得（回應 Xie「容量受限效益下降」的定量版）。
- Turnover 隨容量變小而縮短（1 站 −24%）：容量滿 → 整單派車 → 合併等待少。
- 機制：容量滿時 M5 以 HADGS 式整單組合派車解凍，箱子釋放後回到拆單；1 站 91% 決策時間箱滿，故 pile-on 3.3（接近 HADGS 2.4）。
- v1 對照：1 站 −75%、2 站 −35% 是凍結假象。
- 可信度：10 seeds、40/40 valid、Canon v2；m5_45b 與 m5_pk3 沿用 v1（未觸頂，逐位相同）；Excel 通過。
