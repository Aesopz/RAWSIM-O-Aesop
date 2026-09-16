# 2026-09-16_m4g_legacy_no_distance

- 類別：正式實驗（10 seeds）
- 產生：2026-09-16T02:29:39・正典 v1・DLL 94a44bdb4844・git retro
- 場次狀態：valid=60

## 為何做這組實驗

**問題**：把距離項從目標式移除（w1=0，限制式不變）後，PS/TA 的表現如何變化？距離定價對揀貨流程是否重要？

**動機**：回應教授「把距離放進目標式不太對」的質疑：同一可行域、同一組 Jiao 權重，只把 w1 由 1 改 0；主檢定為 Legacy vs Legacy-NoDist，另附對 M4G 的參考比較。

## 設定

- **m4g_6b**：m4g.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m4g_legacy_6b**：m4g_legacy.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`
- **m4g_legacy_nodist_6b**：m4g_legacy_nodist.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`；`+    <LegacyDistanceWeight>0</LegacyDistanceWeight>`
- **m4g_10b**：m4g.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m4g_legacy_10b**：m4g_legacy.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`
- **m4g_legacy_nodist_10b**：m4g_legacy_nodist.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`；`+    <LegacyDistanceWeight>0</LegacyDistanceWeight>`

比較：
- m4g_legacy_nodist_6b vs m4g_legacy_6b（ablation；變數 LegacyDistanceWeight 1 -> 0 (distance removed from objective)）
- m4g_legacy_nodist_6b vs m4g_6b（benchmark；變數 no-distance Jiao objective vs measured ratio objective）
- m4g_legacy_nodist_10b vs m4g_legacy_10b（ablation；變數 LegacyDistanceWeight 1 -> 0 (distance removed from objective)）
- m4g_legacy_nodist_10b vs m4g_10b（benchmark；變數 no-distance Jiao objective vs measured ratio objective）

## 結果

```
2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m4g_6b,6b,1419.9,946.1,599.1,5.338,112.3,0.1875,9.37,1.190,830.8,1.45
m4g_legacy_6b,6b,1407.8,939.5,589.7,4.414,133.6,0.2266,11.17,1.429,858.9,2.32
m4g_legacy_nodist_6b,6b,1395.9,933.1,589.6,4.357,135.5,0.2299,11.76,1.478,855.6,3.13
m4g_10b,10b,1428.7,952.2,603.5,5.118,118.0,0.1956,10.35,1.317,787.8,0.86
m4g_legacy_10b,10b,1428.1,959.2,602.5,2.939,205.1,0.3405,15.69,2.039,736.2,0.91
m4g_legacy_nodist_10b,10b,1422.1,955.4,597.4,2.982,200.4,0.3355,16.32,2.091,700.4,1.32

paired m4g_legacy_nodist_6b vs m4g_legacy_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.85,−0.68,−0.02,−1.31,+1.42,+1.46,+5.31,+3.40,−0.38,+35.16
p,.108,.194,.977,.426,.279,.403,.004,.029,.886,.107
sig,,,,,,,**,*,,

paired m4g_legacy_nodist_6b vs m4g_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−1.69,−1.37,−1.59,−18.38,+20.66,+22.60,+25.51,+24.23,+2.99,+115.41
p,.005,.006,.002,< .001,< .001,< .001,< .001,< .001,.247,.004
sig,**,**,**,***,***,***,***,***,,**

paired m4g_legacy_nodist_10b vs m4g_legacy_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.42,−0.40,−0.85,+1.45,−2.29,−1.47,+4.00,+2.55,−4.85,+44.49
p,< .001,.303,.092,.329,.083,.313,.040,.168,.103,< .001
sig,***,,,,,,*,,,***

paired m4g_legacy_nodist_10b vs m4g_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.46,+0.34,−1.01,−41.73,+69.83,+71.55,+57.63,+58.76,−11.09,+53.73
p,< .001,.278,.005,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,,**,***,***,***,***,***,***,***

excel cross-check: {"status": "passed", "checks": 200, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

- 結論：在 Jiao 權重目標式下把距離項移除（w1 1→0，限制式不變），幾乎沒有影響：主檢定 NoDist vs Legacy 只有 m/line +5.3%**（6b）/ +4.0%*（10b）、EOR +3.4%*（6b）/ n.s.（10b）；件數、訂單、pile-on、趟次全 n.s.（10b 件數 −0.42%*** 但量級可忽略）。
- 機制：加權和裡 −40 與 1000 的量級遠大於距離，距離只在完成數相同的候選間當 tie-breaker；拿掉它幾乎不改變決策。
- 對照 M4G：NoDist 相對 M4G 趟次 +20.7%*** / +69.8%***、EOR +24.2%*** / +58.8%***。三組並排（10b）：NoDist pile-on 2.98 / Legacy 2.94 / M4G 5.12。
- 回應教授「把距離放進目標式不太對」：以加權和加入距離確實無效，但距離以比值分子（每單位進度的公尺）計價時效果巨大。距離不是不重要，是加權和用錯方式。
- 可信度：10 seeds、60/60 valid、Canon v1、bin_legacydist（守門員逐位相同）、Excel 通過。Legacy 對照沿用實驗 A。
- 口徑：Adding distance as a weighted term changes nothing; pricing it as a ratio halves the trips.
