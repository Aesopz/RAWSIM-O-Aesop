# 2026-09-15_canon_v1_large_split_limit_m5

- 類別：正式實驗（10 seeds）
- 產生：2026-09-15T17:45:51・正典 v1・DLL 94a44bdb4844・git retro
- 場次狀態：valid=40

## 為何做這組實驗

**問題**：Canon v1 大規模（45 bots）：限制每張訂單最多拆 N 份（N=2/3/4）相對不設限的 HGS-M5 有何影響？

**動機**：教授問拆單次數上限的曲線（大規模端）；M5 加入 DrawsFirstDispatch 與 M1G 閘門後需重建正式數據，也是 M5 vs HADGS 主結果的 M5 端。

## 設定

- **m5_45b**：hgs_m5_k10.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
- **m5_n2_45b**：hgs_m5_k10_n2.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <MaxPartsPerOrder>2</MaxPartsPerOrder>`
- **m5_n3_45b**：hgs_m5_k10_n3.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <MaxPartsPerOrder>3</MaxPartsPerOrder>`
- **m5_n4_45b**：hgs_m5_k10_n4.xconf・GreedyM5Configuration・seeds [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  - 45 bots・2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
  - 相對 `Canon/large/hgs_m5_k10.xconf`：`+    <MaxPartsPerOrder>4</MaxPartsPerOrder>`

比較：
- m5_n2_45b vs m5_45b（ablation；變數 MaxPartsPerOrder=2）
- m5_n3_45b vs m5_45b（ablation；變數 MaxPartsPerOrder=3）
- m5_n4_45b vs m5_45b（ablation；變數 MaxPartsPerOrder=4）

## 結果

```
2 h · 10 seeds · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock
policy,bots,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
m5_45b,45b,8318.2,5396.9,3385.3,7.625,444.1,0.1312,20.45,3.601,395.2,3.79
m5_n2_45b,45b,4431.8,2876.1,1846.7,6.797,271.9,0.1472,28.05,4.838,452.3,48.71
m5_n3_45b,45b,6250.6,4049.2,2542.3,7.680,331.2,0.1303,22.40,3.950,471.7,27.70
m5_n4_45b,45b,8266.6,5360.6,3360.4,7.613,441.5,0.1314,20.50,3.611,395.4,4.39

paired m5_n2_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−46.72,−46.71,−45.45,−10.85,−38.78,+12.23,+37.19,+34.35,+14.47,+1185.91
p,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001,< .001
sig,***,***,***,***,***,***,***,***,***,***

paired m5_n3_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−24.86,−24.97,−24.90,+0.73,−25.42,−0.67,+9.54,+9.71,+19.37,+631.22
p,< .001,< .001,< .001,.437,< .001,.476,< .001,< .001,< .001,< .001
sig,***,***,***,,***,,***,***,***,***

paired m5_n4_45b vs m5_45b (delta% = (mean_b - mean_a)/mean_a; n=10)
stat,Items,Lines,Orders,Pile-on,Trips,Trips/Orders,m/Line,EOR(kJ/order),TurnoverMedian(s),StationIdle(%)
pct,−0.62,−0.67,−0.74,−0.16,−0.59,+0.15,+0.25,+0.29,+0.07,+15.89
p,.034,.030,.029,.786,.212,.785,.402,.348,.749,.032
sig,*,*,*,,,,,,,*

excel cross-check: {"status": "passed", "checks": 140, "rel_tol": 1e-09}
```

## 分析（Claude 當下判讀）

**拆單份數上限 N（HGS-M5，45 bots，對照＝不設限）**
- N=4：件數 −0.62%*，其餘多為 n.s. ⟹ 實務上等同不設限。
- N=3：件數 −24.86%***、揀貨站閒置 3.79%→27.70%；N=2：件數 −46.72%***、閒置→48.71%、EOR +34.35%***。
- 大規模對份數上限遠比小規模敏感（小規模 N=3 只 −1.24%）：1000 SKUs、每張單橫跨更多貨架，需要更多份才能湊齊。
- 與 HADGS（N=1 參考，mp1_hadgs，見 docs/experiments/2026-09-15-canon-v1-cross-batch/large_hadgs_reference）：
  - 不設限 M5 vs HADGS：件數 +31.18%***、EOR −53.46%***、每行距離 −50.47%***、pile-on +235%***；訂單週期 +123.92%***（HADGS 176.5 s vs 395.2 s）。
  - N=4 +30.37%***；N=3 件數 −1.42%（n.s.，但 EOR −48.95%***）；N=2 −30.11%*** ⟹ 大規模下至少要允許 3 份才追平 HADGS 吞吐、4 份才取得全部效益。
- 機制假說（未驗證）：同小規模，份數用完的訂單占住槽位，站台空轉。

**可信度**：10 seeds、40/40 valid、Canon v1、Excel 交叉驗證 140/140。HADGS 參考是另一批次（2026-09-15 03:09 前完成，HADGS 程式與設定未變）。
**下一步**：驗證空轉機制；主結果（M5 vs HADGS）週期代價需在論文揭露。
