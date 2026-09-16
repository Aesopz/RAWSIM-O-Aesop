# 2026-09-16_pkwhole_test

- 類別：快速驗證（1 seed）
- 產生：2026-09-16T11:08:43・正典 v1・DLL aee43bfe519f・git 06a384206e
- 場次狀態：valid=1

## 為何做這組實驗

**問題**：開啟 PackingFullWholeOrderFallback 後，1 站 78 箱的 M5 能否退化成整單派車（件數接近 HADGS 6341 而非 2079）？

**動機**：使用者指出容量滿時 M5 應退化成整單決策而非崩潰；行級貪婪缺整單複合動作，此旗標補上。

## 設定

- **m5_pk1_whole_45b**：hgs_m5_k10_pk1_whole.xconf・GreedyM5Configuration・seeds [0]
  - 45 bots・2 h · 1 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <PackingStationCount>1</PackingStationCount>`；`+    <PackingBufferCapacity>78</PackingBufferCapacity>`；`+    <PackingFullWholeOrderFallback>true</PackingFullWholeOrderFallback>`

## 結果

```
2 h · 1 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_pk1_whole_45b,45b,2114.0,1374.0,913.0,4.962,184.0,0.2015,46.43,7.824,660.7,75.59

excel cross-check: {"status": "passed", "checks": 10, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

（待填：這批數據說明了什麼、可信度、下一步）
