# 2026-09-17_large_dyn_iter20_m5

- 類別：快速驗證（1 seed）
- 產生：2026-09-17T23:21:57・正典 v2・DLL 2b11c291972a・git af84d373db
- 場次狀態：valid=1

## 為何做這組實驗

**問題**：大規模動態 M5 將 λ 迭代上限 5→20，結果是否改變？（貪婪／收斂是否為落後常數的槓桿）

**動機**：若 20 次迭代與 5 次結果相同，則貪婪內圈的收斂不保證不是大規模落後固定 λ=13 的原因；差距歸因於比值目標式＋λ̄ 跳躍的結構。對照：formal m5_dyn_45b seed 0（5 次）。

## 設定

- **m5_iter20_45b**：m5_iter20_45b.xconf・GreedyM5Configuration・seeds [0]
  - 45 bots・2 h · 1 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <LambdaIterations>20</LambdaIterations>`

## 結果

```
2 h · 1 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_iter20_45b,45b,8328.0,5381.0,3409.0,7.476,456.0,0.1338,20.78,3.623,381.9,3.69

excel cross-check: {"status": "passed", "checks": 10, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

seed 0：LambdaIterations 5→20，KPI 逐位相同（8,328 件、456 趟、m/line 20.778…），迭代分佈相同 {0:29,1:123,2:184,3:106,4:14,5:3}，20 次上限從未被碰到。
結論：貪婪內圈的收斂預算不是大規模落後固定 λ=13 的原因；每個決策都在 5 次內因 keep-previous 或 tolerance 自然停止。差距歸因於比值目標式＋λ̄ 跳躍在站台瓶頸情境的結構性多派車（見 nojump、60b 兩個 fast）。口試不可宣稱「貪婪無法保證最優故輸給常數」。
