# 2026-09-19_small_ws_ns_no_distance

- 類別：正式實驗（10 seeds）
- 產生：2026-09-19T21:08:13・正典 v2・DLL 2b11c291972a・git e7107871d6
- 場次狀態：valid=40

## 為何做這組實驗

**問題**：不拆單（整單承諾）下，Jiao 加權和目標式拿掉距離項差多少？（M4G-WS-NS₀ vs M4G-WS-NS）

**動機**：拆單情境已知拿掉距離項代價小（WS₀ vs WS：m/line +4–5%、EOR +3%）。補不拆單情境，完成 {距離有/無}×{拆/不拆} 2×2，回答『Jiao 目標式中距離項的作用是否被 1000 空槽罰則壓死』。對照臂沿用 2026-09-16_m4g_ws_ns_vs_ns。

## 設定

- **m4g_ws_ns_6b**：m4g_ws_ns.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`+    <OrderAtomicNoSplit>true</OrderAtomicNoSplit>`；`+    <OrderAtomicCanonPrices>false</OrderAtomicCanonPrices>`；`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`
- **m4g_ws_ns0_6b**：m4g_ws_ns0.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`+    <OrderAtomicNoSplit>true</OrderAtomicNoSplit>`；`+    <OrderAtomicCanonPrices>false</OrderAtomicCanonPrices>`；`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyDistanceWeight>0</LegacyDistanceWeight>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`
- **m4g_ws_ns_10b**：m4g_ws_ns.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`+    <OrderAtomicNoSplit>true</OrderAtomicNoSplit>`；`+    <OrderAtomicCanonPrices>false</OrderAtomicCanonPrices>`；`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`
- **m4g_ws_ns0_10b**：m4g_ws_ns0.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`+    <OrderAtomicNoSplit>true</OrderAtomicNoSplit>`；`+    <OrderAtomicCanonPrices>false</OrderAtomicCanonPrices>`；`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyDistanceWeight>0</LegacyDistanceWeight>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`

比較：
- m4g_ws_ns0_6b vs m4g_ws_ns_6b（ablation；變數 LegacyDistanceWeight 1 -> 0 (no split)）
- m4g_ws_ns0_10b vs m4g_ws_ns_10b（ablation；變數 LegacyDistanceWeight 1 -> 0 (no split)）

## 結果

```
2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m4g_ws_ns_6b,6b,1258.4,820.8,584.3,3.304,176.9,0.3029,14.66,1.678,346.8,12.66
m4g_ws_ns0_6b,6b,1260.0,821.7,585.3,3.400,172.2,0.2944,15.03,1.693,346.2,12.55
m4g_ws_ns_10b,10b,1419.2,922.6,646.7,3.181,203.5,0.3147,16.22,1.903,371.6,1.52
m4g_ws_ns0_10b,10b,1419.9,928.5,648.1,3.219,201.5,0.3110,16.94,1.964,372.5,1.45

paired m4g_ws_ns0_6b vs m4g_ws_ns_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.13,+0.11,+0.17,+2.91,−2.66,−2.79,+2.49,+0.94,−0.19,−0.86
p,.905,.923,.873,.061,.004,.068,.021,.342,.926,.906
sig,,,,,**,,*,,,

paired m4g_ws_ns0_10b vs m4g_ws_ns_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.05,+0.64,+0.22,+1.18,−0.98,−1.18,+4.45,+3.17,+0.22,−4.11
p,.800,.072,.579,.291,.432,.293,< .001,< .001,.909,.747
sig,,,,,,,***,***,,

excel cross-check: {"status": "passed", "checks": 120, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

**結論**：不拆單下拿掉 Jiao 目標式的距離項幾乎沒差：件數 +0.1（n.s.）、m/line +2.5%*／+4.5%***、EOR +0.9（n.s.）／+3.2%***、trips −2.7%**／−1.0、其餘 n.s.。與拆單情境（WS₀ vs WS：m/line +4–5%、EOR +3%）同量級。
**2×2 完成**：{距離有/無}×{拆/不拆}——距離項在加權和目標式裡兩種可行域下都只值 2–5% 距離、≤3% 能耗；相對地目標式形式（加權和→比值）值 24–59% 能耗。結論：Jiao 的距離權重 1 被 1000 的空槽罰則壓死，距離要有作用必須進比值目標式。
可信度：10 seeds、雙尾配對 t、excel 交叉驗證通過；對照臂沿用 2026-09-16_m4g_ws_ns_vs_ns。
