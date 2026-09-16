# 2026-09-16_canon_v2_large_split_limit_m5

- 類別：正式實驗（10 seeds）
- 產生：2026-09-16T19:52:38・正典 v2・DLL 94a44bdb4844・git retro
- 場次狀態：valid=40

## 為何做這組實驗

**問題**：Canon v2（預算耗盡時整單組合派車）下，大規模 45 bots 限制每張訂單最多拆 N=2/3/4 份相對不設限的 HGS-M5 有何影響？

**動機**：Canon v1 的 N≤3 結果被行級貪婪凍結汙染（N=2 seed0 4460 件 → v2 8370）；重跑取得限制拆單的真實代價。對照 m5_45b 預算從未觸頂，沿用 v1 資料（逐位不變）。

## 設定

- **m5_45b**：hgs_m5_k10.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
- **m5_n2_45b**：hgs_m5_k10_n2.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <MaxPartsPerOrder>2</MaxPartsPerOrder>`
- **m5_n3_45b**：hgs_m5_k10_n3.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <MaxPartsPerOrder>3</MaxPartsPerOrder>`
- **m5_n4_45b**：hgs_m5_k10_n4.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <MaxPartsPerOrder>4</MaxPartsPerOrder>`

比較：
- m5_n2_45b vs m5_45b（ablation；變數 MaxPartsPerOrder=2）
- m5_n3_45b vs m5_45b（ablation；變數 MaxPartsPerOrder=3）
- m5_n4_45b vs m5_45b（ablation；變數 MaxPartsPerOrder=4）

## 結果

```
2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_45b,45b,8318.2,5396.9,3385.3,7.625,444.1,0.1312,20.45,3.601,395.2,3.79
m5_n2_45b,45b,8376.2,5434.1,3476.4,5.065,687.4,0.1978,25.37,4.359,358.4,3.12
m5_n3_45b,45b,8296.6,5382.4,3386.9,6.986,484.9,0.1432,21.34,3.725,398.4,4.04
m5_n4_45b,45b,8317.1,5399.7,3385.0,7.608,445.0,0.1315,20.33,3.587,394.3,3.80

paired m5_n2_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.70,+0.69,+2.69,−33.57,+54.78,+50.75,+24.05,+21.04,−9.31,−17.56
p,< .001,.004,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,**,***,***,***,***,***,***,***,***

paired m5_n3_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.26,−0.27,+0.05,−8.38,+9.19,+9.13,+4.35,+3.46,+0.81,+6.72
p,.100,.211,.892,< .001,< .001,< .001,< .001,< .001,.517,.097
sig,,,,***,***,***,***,***,,

paired m5_n4_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.01,+0.05,−0.01,−0.22,+0.20,+0.22,−0.58,−0.38,−0.22,+0.44
p,.792,.438,.894,.652,.669,.657,.081,.150,.470,.748
sig,,,,,,,,,,

excel cross-check: {"status": "passed", "checks": 140, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

- 結論（Canon v2，整單 fallback 生效）：限制拆單份數對**吞吐無損**（N=2 件數 +0.70%***、訂單 +2.69%***；N=3、N=4 n.s.），代價全在效率：N=2 趟次 +54.8%***、EOR +21.0%***、pile-on −33.6%***；N=3 EOR +3.5%***；N=4 與不設限無差異。
- 邊際價值：第 3 份拿到幾乎全部效率增益（EOR 差 3.5%），第 4 份飽和。
- N=2 週期 −9.3%***：拆得少、合併等待少。實務上 N=2 是「用 21% 能耗換 9% 週期」的選項。
- v1 對照：N=2 −46.7%、N=3 −24.9% 全是行級貪婪凍結（見 project_canon_v2_m5_whole_order_fallback）；v2 才是限制拆單的真實代價。
- 可信度：10 seeds、40/40 valid、Canon v2；對照 m5_45b 沿用 v1（預算未觸頂，守門員逐位相同）；Excel 交叉驛證通過。
