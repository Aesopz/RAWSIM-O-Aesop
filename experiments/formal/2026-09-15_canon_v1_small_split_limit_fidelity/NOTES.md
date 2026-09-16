# 2026-09-15_canon_v1_small_split_limit_fidelity

- 類別：正式實驗（10 seeds）
- 產生：2026-09-15T17:45:41・正典 v1・DLL 94a44bdb4844・git retro
- 場次狀態：valid=100

## 為何做這組實驗

**問題**：Canon v1 小規模：限制每張訂單最多拆 N 份（N=2/3/4）相對不設限的 M4G 有何影響？HGS-M5 相對 M4G 損失多少？

**動機**：教授問拆單次數上限的曲線；M1G 閘門烘入正典（Canon v1）後需重建正式數據；M5 保真度是大規模結果能否代表 M4G 的前提。

## 設定

- **m4g_6b**：m4g.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m5_6b**：hgs_m5.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m4g_n2_6b**：m4g_n2.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`+    <MaxPartsPerOrder>2</MaxPartsPerOrder>`
- **m4g_n3_6b**：m4g_n3.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`+    <MaxPartsPerOrder>3</MaxPartsPerOrder>`
- **m4g_n4_6b**：m4g_n4.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 6 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`+    <MaxPartsPerOrder>4</MaxPartsPerOrder>`
- **m4g_10b**：m4g.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m5_10b**：hgs_m5.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- **m4g_n2_10b**：m4g_n2.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`+    <MaxPartsPerOrder>2</MaxPartsPerOrder>`
- **m4g_n3_10b**：m4g_n3.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`+    <MaxPartsPerOrder>3</MaxPartsPerOrder>`
- **m4g_n4_10b**：m4g_n4.xconf・M4GConfiguration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 10 bots・2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
  - 相對 `Canon/small/m4g.xconf`：`+    <MaxPartsPerOrder>4</MaxPartsPerOrder>`

比較：
- m5_6b vs m4g_6b（fidelity；變數 exact MILP (M4G) vs greedy mirror (HGS-M5)）
- m4g_n2_6b vs m4g_6b（ablation；變數 MaxPartsPerOrder=2）
- m4g_n3_6b vs m4g_6b（ablation；變數 MaxPartsPerOrder=3）
- m4g_n4_6b vs m4g_6b（ablation；變數 MaxPartsPerOrder=4）
- m5_10b vs m4g_10b（fidelity；變數 exact MILP (M4G) vs greedy mirror (HGS-M5)）
- m4g_n2_10b vs m4g_10b（ablation；變數 MaxPartsPerOrder=2）
- m4g_n3_10b vs m4g_10b（ablation；變數 MaxPartsPerOrder=3）
- m4g_n4_10b vs m4g_10b（ablation；變數 MaxPartsPerOrder=4）

## 結果

```
2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m4g_6b,6b,1419.9,946.1,599.1,5.338,112.3,0.1875,9.37,1.190,830.8,1.45
m5_6b,6b,1422.2,931.2,584.0,5.231,111.7,0.1913,9.40,1.203,916.9,1.30
m4g_n2_6b,6b,1294.9,863.0,558.9,5.023,111.4,0.1994,9.58,1.189,783.7,10.13
m4g_n3_6b,6b,1402.3,934.7,595.4,5.306,112.3,0.1887,9.35,1.177,809.2,2.70
m4g_n4_6b,6b,1418.1,948.0,599.1,5.402,111.0,0.1853,9.23,1.172,796.4,1.60
m4g_10b,10b,1428.7,952.2,603.5,5.118,118.0,0.1956,10.35,1.317,787.8,0.86
m5_10b,10b,1430.3,934.2,588.1,5.081,115.8,0.1970,10.18,1.312,955.2,0.74
m4g_n2_10b,10b,1305.5,872.3,565.7,4.794,118.1,0.2088,10.47,1.306,796.7,9.40
m4g_n3_10b,10b,1423.0,948.4,601.5,5.065,118.9,0.1977,10.24,1.309,791.9,1.24
m4g_n4_10b,10b,1429.1,952.4,604.0,5.104,118.4,0.1960,10.24,1.301,774.1,0.81

paired m5_6b vs m4g_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.16,−1.57,−2.52,−2.00,−0.53,+1.99,+0.30,+1.15,+10.36,−10.64
p,.485,.005,< .001,.034,.531,.037,.736,.213,.003,.482
sig,,**,***,*,,*,,,**,

paired m4g_n2_6b vs m4g_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−8.80,−8.78,−6.71,−5.90,−0.80,+6.32,+2.17,−0.05,−5.67,+596.98
p,< .001,< .001,< .001,.007,.515,.008,.095,.973,.015,< .001
sig,***,***,***,**,,**,,,*,***

paired m4g_n3_6b vs m4g_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−1.24,−1.20,−0.62,−0.59,0.00,+0.60,−0.27,−1.07,−2.60,+86.00
p,.004,.027,.160,.587,1.000,.585,.767,.242,.242,.003
sig,**,*,,,,,,,,**

paired m4g_n4_6b vs m4g_6b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.13,+0.20,0.00,+1.20,−1.16,−1.18,−1.51,−1.44,−4.13,+9.92
p,.424,.480,1.000,.442,.417,.441,.256,.288,.051,.334
sig,,,,,,,,,,

paired m5_10b vs m4g_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.11,−1.89,−2.55,−0.72,−1.86,+0.71,−1.60,−0.39,+21.25,−14.07
p,.078,< .001,< .001,.393,.051,.402,.086,.647,< .001,.037
sig,,***,***,,,,,,***,*

paired m4g_n2_10b vs m4g_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−8.62,−8.39,−6.26,−6.32,+0.08,+6.75,+1.14,−0.83,+1.14,+998.47
p,< .001,< .001,< .001,< .001,.952,.001,.343,.492,.651,< .001
sig,***,***,***,***,,**,,,,***

paired m4g_n3_10b vs m4g_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.40,−0.40,−0.33,−1.03,+0.76,+1.11,−1.07,−0.63,+0.52,+44.70
p,.003,.348,.374,.382,.569,.351,.170,.464,.837,.002
sig,**,,,,,,,,,**

paired m4g_n4_10b vs m4g_10b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,+0.03,+0.02,+0.08,−0.27,+0.34,+0.25,−1.09,−1.25,−1.74,−5.00
p,.696,.946,.760,.841,.814,.855,.262,.192,.620,.560
sig,,,,,,,,,,

excel cross-check: {"status": "passed", "checks": 360, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

**A. 拆單份數上限 N（M4G，對照＝不設限）**
- N=4 與不設限無可測量差異（6b、10b 全部 n.s.）；N=3 件數 −1.24%**（6b）／−0.40%**（10b），其餘多為 n.s.；N=2 件數 −8.80%***（6b）／−8.62%***（10b）。
- N=2 的損失伴隨揀貨站閒置暴增（6b 1.45%→10.13%、10b 0.86%→9.40%），每行距離與 EOR 無顯著變化 ⟹ 損失是「站台空轉」，不是「路線變差」。
- 機制假說（未驗證）：訂單用完份數後，剩餘行必須一次湊齊才能出貨，湊不齊時占住站台槽位，站台有槽無貨可揀。可用 m4g_split_lifetime.csv 驗證。
- 對照 N=1（M4G-NS，tau10 批次，見 docs/experiments/2026-09-15-canon-v1-cross-batch/small_n1_reference）：6b 時 N=2 仍比不拆單多 +12.86%***；10b 時 N=2 反而比不拆單少 −7.49%*** ⟹ 機器人充足時，「只准拆兩份」比「完全不拆」更差；曲線不是單調的。
- 與舊正典（閘門烘入前）比較：N=2 件數損失從 −11.3%／−8.1% 變為 −8.80%／−8.62%，方向與量級一致。

**B. HGS-M5 保真度（對照＝M4G）**
- 件數無顯著差異（+0.16%、+0.11%）；行數 −1.57%**／−1.89%***、訂單 −2.52%***／−2.55%***；EOR、每行距離 n.s.；訂單週期 +10.36%**／+21.25%***。
- 結論：M5 在吞吐與能耗上是 M4G 的忠實近似，代價是訂單數略少、週期較長 ⟹ 大規模 M5 結果是 M4G 的保守下界（週期除外）。

**可信度**：10 seeds、100/100 valid、Canon v1、同一份 DLL（守門員逐位重現）、Excel 交叉驗證 360/360。retro 匯入，設定檔逐行比對 Canon。
**下一步**：驗證 N=2 站台空轉的機制（不需跑模擬）；N=1 參考點來自另一批次，引用時註明。

**2026-09-16 補充：拆單型態（同期跨站 vs 跨期）**
- 依 `m4g_split_lifetime.csv` 的 `stationsInDecision`（同一決策內分到幾站）重算 10 seeds：被拆訂單 34%；其中同期跨站 6b 0.7%、10b 0.1%；跨期 99.4%／99.9%。
- 結論：訂單口徑站台容量下，拆單本質是「跨期先做一部分」，同期跨站能力休眠。與 2026-08-07 舊量測一致。
- ⚠️ 2026-09-16 早上曾用 `stationId nunique` 算出 41% 跨站，是口徑錯誤（把跨期分到不同站也計入），已更正。
