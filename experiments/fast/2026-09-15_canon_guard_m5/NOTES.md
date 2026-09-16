# 2026-09-15_canon_guard_m5

- 類別：快速驗證（1 seed）
- 產生：2026-09-15T16:59:13・正典 v1・DLL 94a44bdb4844・git 06a384206e
- 場次狀態：valid=1

## 為何做這組實驗

**問題**：Canon 範本（誠實命名）跑 M5 小規模 seed 0，結果是否與 out/gc_m5_6bot_s0（同內容、舊檔名）逐位相同？並驗證 exp.py 全流程。

**動機**：exp.py 建立後第一次實跑；同時確認 Canon 範本改名不改變模擬行為。

## 設定

- **hgs_m5_6b**：hgs_m5.xconf・GreedyM5Configuration・seeds [0]
  - 6 bots・2 h · 1 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock

## 結果

```
2 h · 1 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
hgs_m5_6b,6b,1425.0,928.0,601.0,5.272,114.0,0.1897,9.56,1.180,903.7,1.15

excel cross-check: {"status": "passed", "checks": 10, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

- 結論：Canon 範本（誠實命名）與舊檔名跑出的模擬**逐位相同**；改名不影響行為。exp.py 的 run → wait → verify → table 全流程首次實跑通過。
- 比對對象：out/gc_m5_6bot_s0（同日 Canon v1 設定，CoreBenchmark 舊檔名；xconf 只差換行、xlayo/xsett 只差名稱行）。
- 逐位相同：stationstatistics.csv、kpi_report.csv、orderprogression.csv、tripscompleted.csv、itemprogression.csv、pod_visit_metrics.csv。
- statistics.txt 只有牆鐘計時（StatTiming*、StatRealTimeUsed）與記憶體不同；footprint.csv 只差名稱欄與同一批計時欄。這些是機器負載造成，不是模擬結果。
- verify：valid（exit 0、時長 2 h、控制器 OBGREEDYM5、bots/pods/站數/SKU 相符、快照 SHA 與 DLL 一致）。統計管線 Excel 交叉驗證 10/10 passed。
- 可信度：1 seed 足以回答「是否逐位相同」這類是非題。
- 下一步：無；Canon 範本可正式取代 CoreBenchmark 作為跑批來源。
