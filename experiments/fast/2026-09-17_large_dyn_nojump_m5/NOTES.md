# 2026-09-17_large_dyn_nojump_m5

- 類別：快速驗證（1 seed）
- 產生：2026-09-17T20:51:14・正典 v2・DLL 2b11c291972a・git f86bd35785
- 場次狀態：valid=1

## 為何做這組實驗

**問題**：大規模動態 M5 關掉 UpperBoundJump，能否追平事後最佳常數 λ=13？

**動機**：檢驗大規模動態落後 λ=13/20 <1% 是否來自 λ̄ 跳躍（27% 派車決策、32% 新趟距離發生在 λ̄≈42）。單 seed 探風向；動態與 λ=13 的 seed 0 已存在可直接對照。

## 設定

- **m5_nojump_45b**：m5_nojump_45b.xconf・GreedyM5Configuration・seeds [0]
  - 45 bots・2 h · 1 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`

## 結果

```
2 h · 1 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_nojump_45b,45b,6342.0,4113.0,2594.0,7.411,350.0,0.1349,20.66,3.702,401.2,26.62

excel cross-check: {"status": "passed", "checks": 10, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

seed 0 對照（同 seed 的 formal 資料）：動態有跳躍 8,328 件／閒置 3.7%；**無跳躍 6,342 件（−24%）／閒置 26.6%**；固定 13：8,337；固定 20：8,357。
結論：上界跳躍在大規模仍不可省略——假設「動態落後 λ=13/20 是跳躍過度派車」被否證。真正機制：Dinkelbach 的比值最佳 λ*（中位 8.3）偏保守，供不上 12 站，需靠跳躍（27% 派車決策、λ̄≈42）離散補價；固定 13–20 是恆定中價，派車節奏平順、pile-on 較高。差距 <1%。
1 seed 探風向即足夠（效應 −24% 遠超 seed 噪音 0.2%）；不需升級 formal。
