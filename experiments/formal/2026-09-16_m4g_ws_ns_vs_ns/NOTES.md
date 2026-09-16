# 2026-09-16_m4g_ws_ns_vs_ns

- 類別：正式實驗（10 seeds）
- 產生：2026-09-16T20:07:52・正典 v2・DLL 2b11c291972a・git e7107871d6
- 場次狀態：valid=40

## 為何做這組實驗

**問題**：整單承諾（不拆單）下，Jiao 加權和目標式（M4G-WS-NS）與比值目標式（M4G-NS）表現差異？補 M4G 架構 2×2（拆單 × 目標式）的第四格。

**動機**：seed 0 顯示不拆單時比值目標式無優勢（EOR +2.5%），拆單時優勢兩倍以上 ⟹ 定價是拆單的放大器；需 10 seeds 確認交互作用。對照 M4G-NS 沿用 tau10_ns。

## 設定

- **m4g_ns_6b**：m4g_ns.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m4g_ws_ns_6b**：m4g_ws_ns.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <OrderAtomicCanonPrices>true</OrderAtomicCanonPrices>`；`+    <OrderAtomicCanonPrices>false</OrderAtomicCanonPrices>`；`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`
- **m4g_ns_10b**：m4g_ns.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m4g_ws_ns_10b**：m4g_ws_ns.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g_ns.xconf`：`-    <OrderAtomicCanonPrices>true</OrderAtomicCanonPrices>`；`+    <OrderAtomicCanonPrices>false</OrderAtomicCanonPrices>`；`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`

比較：
- m4g_ns_6b vs m4g_ws_ns_6b（ablation；變數 objective: Jiao weighted sum vs measured ratio prices; whole-order commitment, same feasible region）
- m4g_ns_10b vs m4g_ws_ns_10b（ablation；變數 objective: Jiao weighted sum vs measured ratio prices; whole-order commitment, same feasible region）

## 結果

```
2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m4g_ns_6b,6b,1147.3,748.1,523.2,3.083,169.8,0.3252,14.84,1.746,435.5,20.37
m4g_ws_ns_6b,6b,1258.4,820.8,584.3,3.304,176.9,0.3029,14.66,1.678,346.8,12.66
m4g_ns_10b,10b,1411.2,919.0,634.5,3.208,198.2,0.3125,15.06,1.804,428.8,2.05
m4g_ws_ns_10b,10b,1419.2,922.6,646.7,3.181,203.5,0.3147,16.22,1.903,371.6,1.52

paired m4g_ns_6b vs m4g_ws_ns_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−8.83,−8.86,−10.46,−6.69,−4.01,+7.36,+1.19,+4.05,+25.56,+60.86
p,< .001,< .001,< .001,< .001,.021,< .001,.326,.009,< .001,< .001
sig,***,***,***,***,*,***,,**,***,***

paired m4g_ns_10b vs m4g_ws_ns_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.56,−0.39,−1.89,+0.84,−2.60,−0.69,−7.15,−5.24,+15.37,+35.42
p,.049,.211,< .001,.604,.112,.659,< .001,< .001,< .001,.060
sig,*,,***,,,,***,***,***,

excel cross-check: {"status": "passed", "checks": 120, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

- 結論：整單承諾下，比值目標式（M4G-NS）相對加權和（M4G-WS-NS）**沒有優勢**：6b 件數 −8.83%***、EOR +4.05%**；10b 件數 −0.56%*、EOR −5.24%***、m/line −7.15%***。
- 2×2（M4G 架構）完成：目標式效果在不拆單下為 −8.8%/+4.1%（6b 件數/EOR），在拆單下為 +0.9%/−16.8%；拆單效果在加權和下 +11.9%/−14.8%，在比值下 +23.8%/−31.9%。**交互作用**：定價的價值來自行級拆單，兩者互相放大。
- 解釋第 31 頁：M1G→M4G-NS 在 6b 輸 7.65% 不是 M4G 公式的問題（WS-NS 比 M1G 好 2.8%），是比值目標式在不拆單、機器人稀缺時過度挑剔（λ 只透過 n(o) 起作用）。
- 代價：比值組 Turnover 較長（+15～26%***）。
- 命名：書面 M4G-WS-NS；旗標 OrderAtomicNoSplit＋LegacyObjective（OrderAtomicCanonPrices=false）。
- 可信度：10 seeds、40/40 valid；對照 M4G-NS 沿用 tau10_ns（v1 Release，M4G 路徑與 v2 相同）；Excel 通過。
- 用途：方法論／Appendix 的 2×2 表，論證「定價是拆單的放大器」。
