# 2026-09-17_canon_v2_small_m5_guard

- 類別：快速驗證（1 seed）
- 產生：2026-09-17T00:28:31・正典 v2・DLL 2b11c291972a・git 514ebf3f6b
- 場次狀態：valid=2

## 為何做這組實驗

**問題**：Canon v2 建置下小規模 M5（N、包裝皆無上限）是否與 v1 run 逐位相同？

**動機**：還原 canon_v1_small_split_limit_fidelity 的 20 個 M5 run（heuristic validation 數據）；fallback 在預算不綁定時不應觸發。

## 設定

- **m5_6b**：hgs_m5.xconf・GreedyM5Configuration・seeds [0]
  - 6 bots・2 h · 1 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m5_10b**：hgs_m5.xconf・GreedyM5Configuration・seeds [0]
  - 10 bots・2 h · 1 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock

## 結果

```
2 h · 1 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_6b,6b,1425.0,928.0,601.0,5.272,114.0,0.1897,9.56,1.180,903.7,1.15
m5_10b,10b,1430.0,923.0,591.0,4.884,121.0,0.2047,10.64,1.339,867.1,0.77

excel cross-check: {"status": "passed", "checks": 20, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

Result: PASS. For both fleets the v2 build reproduces the v1 seed-0 statistics.txt line-for-line (663 / 807 lines) except StatTiming*, StatRealTimeUsed and StatMaxMemoryUsed (wall-clock only). PackingFullWholeOrderFallback only enters when the split budget binds; with unbounded N and packing it is dead code, as designed.
Action: the 20 M5 runs of 2026-09-15_canon_v1_small_split_limit_fidelity restored to valid with note canon-equivalent(v2, guard this experiment). Heuristic-validation (M5 vs M4G, small) numbers are therefore Canon v2 legal without rerun.
