# 2026-09-16_m1g_split_legacy_objective_smoke

- 類別：快速驗證（1 seed）
- 產生：2026-09-16T01:00:29・正典 v1・DLL 94a44bdb4844・git 06a384206e
- 場次狀態：valid=1

## 為何做這組實驗

**問題**：M1G-S（M4G 可行域允許拆單＋M1G 固定權重目標式 w1=1/w2=−40/w3=1000，忠實估值鏡像）在 Canon v1 小規模 6 bots seed 0 能否正常跑完？行為是否合理（有拆單、無停擺）？

**動機**：補 2×2 消融（拆單 × 定價）的第四格；正式 10 seeds 前先 1 seed 確認可行。

## 設定

- **m1g_split_6b**：m1g_split.xconf・M4GConfiguration・seeds [0]
  - 6 bots・2 h · 1 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`-    <LegacyObjective>false</LegacyObjective>`；`+    <LegacyObjective>true</LegacyObjective>`；`+    <LegacyOrderReward>-40</LegacyOrderReward>`；`+    <LegacyIdleSlotWeight>1000</LegacyIdleSlotWeight>`；`+    <LegacyRewardValuation>true</LegacyRewardValuation>`

## 結果

```
2 h · 1 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m1g_split_6b,6b,1388.0,931.0,612.0,4.467,137.0,0.2239,11.51,1.394,822.3,3.65

excel cross-check: {"status": "passed", "checks": 10, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

（待填：這批數據說明了什麼、可信度、下一步）
