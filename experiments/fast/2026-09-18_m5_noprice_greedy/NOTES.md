# 2026-09-18_m5_noprice_greedy

- 類別：快速驗證（1 seed）
- 產生：2026-09-18T01:33:31・正典 v2・DLL 2b11c291972a・git 06ce59862b
- 場次狀態：valid=2

## 為何做這組實驗

**問題**：拿掉價格的純貪婪拆單（λ→∞，距離只當平手）表現如何？M5 贏 HADGS 是因為拆單還是因為定價？

**動機**：使用者假設 M5 贏 HADGS 主因是拆單而非定價。以 LambdaFixed=10000（距離權重≈0，對應 M4G-WS₀）做『有拆單無價格』的貪婪；小 6b 對照動態／固定 9，大 45b 對照動態／固定 13／HADGS。1 seed 探風向。

## 設定

- **m5_noprice_6b**：m5_noprice_6b.xconf・GreedyM5Configuration・seeds [0]
  - 6 bots・2 h · 1 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/hgs_m5.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`
- **m5_noprice_45b**：m5_noprice_45b.xconf・GreedyM5Configuration・seeds [0]
  - 45 bots・2 h · 1 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`-    <LambdaFixed>0</LambdaFixed>`；`+    <LambdaFixed>10000</LambdaFixed>`；`+    <LambdaIterations>0</LambdaIterations>`；`+    <MuFixed>16208</MuFixed>`；`-    <UpperBoundJump>true</UpperBoundJump>`；`+    <UpperBoundJump>false</UpperBoundJump>`

## 結果

```
WARNING: arms differ in scenario beyond bots; scenario line shows the first arm
2 h · 1 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_noprice_6b,6b,1418.0,930.0,615.0,5.913,104.0,0.1691,9.66,1.157,825.8,1.59
m5_noprice_45b,45b,8061.0,5265.0,3343.0,8.316,402.0,0.1203,21.98,3.965,396.5,6.74

excel cross-check: {"status": "passed", "checks": 20, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

seed 0。小 6b：無價格 1,418 件／EOR 1.157 vs 動態 1,425／1.180、固定 9 1,419／1.167、M4G 動態 1,426／1.197 → 價格在小規模 M5 幾乎無作用。大 45b：無價格 8,061 件／m/line 21.98／EOR 3.97／閒置 6.7% vs 動態 8,328／20.78／3.62／3.7%，但仍大勝 HADGS 6,528／39.27／7.40／24.5%。
結論：M5 對 HADGS 的 +31% 件數中約 23 pp 來自拆單＋邊際線貪婪結構、約 8 pp 來自價格；價格在 M5 是微調（大規模 3% 件、6–9% 效率），在 M4G 是主角（贏固定 15–44%）。使用者假設「M5 贏是因為拆單」成立。無價格版失敗模式：偶派遠貨架、站台閒置升至 6.7%。
