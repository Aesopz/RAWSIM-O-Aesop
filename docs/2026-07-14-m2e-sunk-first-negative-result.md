# M2e-SF 五 Seed 負結果與結構診斷

日期：2026-07-14  
狀態：**Gate 3 FAIL，保留為 default-off 實驗 arm**  
設計：`docs/superpowers/specs/2026-07-14-m2e-sunk-first-design.md`

## 1. 判決

`SunkFirstScoring` 的模型語意、two-solve 鎖值與 online runtime 均成立，但預先固定的五 seed 硬門檻全部失敗：

| Arm | TP | PO | IPO | RD | OD | EOR | avg residency |
|---|---:|---:|---:|---:|---:|---:|---:|
| M2e | 642.4 | 2.051 | 4.567 | 18,616.5 | 28.988 | 2.402 | 132.9 s |
| M2e-SF | 633.0 | 2.078 | 4.687 | 18,577.9 | 29.355 | 2.433 | 137.8 s |
| PVGS-E | 641.2 | 3.703 | 8.168 | 13,609.4 | 21.233 | 1.735 | 116.7 s |

因此：

- `633.0 < 641.2`，TP FAIL。
- `2.078 < 3.703`，PO FAIL。
- `4.687 < 8.168`，IPO FAIL。

相較 M2e baseline，SF 只把 PO 提高約 1.3%、IPO 提高約 2.6%，但 TP 降低 9.4 orders（約 1.5%）。這不是可接受的 throughput-preserving pile-on 改善。

## 2. 各 Seed 固定臂結果

| Arm | Seed | TP | Items | PO | IPO | RD | OD | EOR | avg residency |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| M2e | 0 | 657 | 1428 | 2.133 | 4.636 | 18507 | 28.17 | 2.349 | 125.9 |
| M2e | 1 | 623 | 1431 | 1.935 | 4.444 | 18735 | 30.07 | 2.479 | 139.9 |
| M2e | 2 | 664 | 1429 | 2.049 | 4.410 | 19341 | 29.13 | 2.418 | 132.0 |
| M2e | 3 | 629 | 1430 | 2.111 | 4.799 | 18051 | 28.70 | 2.364 | 136.2 |
| M2e | 4 | 639 | 1432 | 2.029 | 4.546 | 18449 | 28.87 | 2.399 | 130.7 |
| M2e-SF | 0 | 638 | 1425 | 2.148 | 4.798 | 17895 | 28.05 | 2.342 | 129.1 |
| M2e-SF | 1 | 623 | 1430 | 1.984 | 4.554 | 19075 | 30.62 | 2.514 | 153.8 |
| M2e-SF | 2 | 638 | 1431 | 2.071 | 4.646 | 18585 | 29.13 | 2.432 | 137.1 |
| M2e-SF | 3 | 635 | 1430 | 2.138 | 4.815 | 18663 | 29.39 | 2.426 | 132.4 |
| M2e-SF | 4 | 631 | 1424 | 2.049 | 4.623 | 18671 | 29.59 | 2.453 | 136.5 |
| PVGS-E | 0 | 658 | 1423 | 3.871 | 8.371 | 13163 | 20.01 | 1.624 | 114.8 |
| PVGS-E | 1 | 621 | 1409 | 3.810 | 8.644 | 13083 | 21.07 | 1.715 | 122.8 |
| PVGS-E | 2 | 659 | 1427 | 3.702 | 8.017 | 14327 | 21.74 | 1.789 | 120.7 |
| PVGS-E | 3 | 622 | 1382 | 3.418 | 7.593 | 13783 | 22.16 | 1.806 | 110.2 |
| PVGS-E | 4 | 646 | 1429 | 3.713 | 8.213 | 13691 | 21.19 | 1.740 | 115.3 |

M2e/PVGS-E 使用既有 `output_acc_*_s0..4` 固定參考臂；本輪先重跑 seed 0，六個核心統計與既有 seed-0 檔完全一致後，沿用其餘固定存檔。M2e-SF 五個 seeds 均為本輪新跑，stdout 皆以 `.Fin. - SUCCESS` 結束，stderr 均為 0 bytes。

## 3. 機制確實觸發

五 seeds 共 3,633 次 exact decisions：

- `sunkOrdersStar > 0`：1,388 次，38.2%。
- `H*` 每決策平均 0.419，最大 5。
- Solve 1：3,633 次皆實際執行。
- final lock violation：0。
- `h_o=1` 卻使用新 `P_a` supply：0。
- final 每決策平均 sunk units：1.233。
- final 每決策平均 new trips：0.425。

所以負結果不是旗標未生效，也不是 `h_o` 定義或鎖值失敗。

## 4. Od 觀測

| Seed | decisions | H*>0 | H% | Od fired | Od% | 同時發生 |
|---:|---:|---:|---:|---:|---:|---:|
| 0 | 736 | 281 | 38.2% | 223 | 30.3% | 0 |
| 1 | 697 | 285 | 40.9% | 108 | 15.5% | 0 |
| 2 | 715 | 296 | 41.4% | 129 | 18.0% | 0 |
| 3 | 749 | 251 | 33.5% | 269 | 35.9% | 0 |
| 4 | 736 | 275 | 37.4% | 215 | 29.2% | 0 |

Od replacement 共觸發 944/3,633 次（26.0%），不是可忽略路徑；且 `odFired=1` 與 `H*>0` 在五 seeds 中零重疊。這符合目前 admission replacement 會排除站上 `P_b` 可服務訂單的既知問題。Od 應另立 config-gated amendment 做 `legacy/off/HADGS-sum` 因果比較，不能與本 arm 混寫成 sunk-first 成果。

## 5. 槽位與拆單診斷

| Arm | slot occupancy | mean WIP / 12 | split parents | consolidation wait mean | p95 | max |
|---|---:|---:|---:|---:|---:|---:|
| M2e | 98.8% | 11.85 | 145 | 222.5 s | 986.2 s | 3781.8 s |
| M2e-SF | 100.9% | 12.11 | 194 | 232.2 s | 1006.2 s | 5779.1 s |
| PVGS-E | 86.6% | 10.40 | 216 | 48.1 s | 119.4 s | 202.2 s |

M2e-SF 比 baseline 多建立 49 個跨期 split parents，並把槽位佔用推過 100%。嚴格 sunk lock 雖降低 pod visits（每 seed output pods handled：311.4 降至 302.8）並略升 station item pile-on（4.597 升至 4.724），但仍遠離 PVGS-E 的 171.4 pod visits / 8.279 pile-on。

關鍵不是「有沒有優先用 sunk pod」，而是何時把 order 綁進 station slot。SF 在 snapshot 內把 sunk-completable order 鎖住，仍沿用 M2e 的早綁定與跨期 child ledger；PVGS-E 則靠 one-claim-resweep 與 release-moment re-harvest，把拆單 child 很快收斂。這解釋了 PVGS-E 即使 output station idle 較高，仍有較低 RD/EOR、較短 residency 與較高 TP。

## 6. Online 求解成本

| Arm/layer | median | p95 | max |
|---|---:|---:|---:|
| M2e total | 0.0320 s | 0.6939 s | 7.5692 s |
| M2e-SF total | 0.0525 s | 0.1948 s | 7.3529 s |
| M2e-SF Solve 1 | 0.0356 s | 0.0475 s | 0.1095 s |
| M2e-SF Solve 2 | 0.0123 s | 0.1501 s | 7.3060 s |

SF median 是 baseline 的 1.64 倍，低於 2.5 倍 guardrail；p95 與 max 也未惡化。不同 policy 產生不同模型軌跡，因此 p95 較低只可解讀為本次實驗觀測，不是 two-solve 的普遍加速主張。

## 7. 結論與下一步

1. 保留 `SunkFirstScoring=false` 預設與 `split_milp_m2e_sunk.xconf`，作為可重現的結構性負結果。
2. 不做 sunk weight sweep；本設計是字典序，事後調權重會破壞 causal arm。
3. 先開 OdMode amendment，因 Od 實際觸發 26.0% 且與 `H*>0` 完全互斥。
4. 主線轉向 release-moment pivotal retention / late binding：規劃 pod-order coverage 可以早做，但 station slot 應在 pod 臨站或 processing release 時才提交。
5. 論文不得宣稱 sunk-first 使 M2e >= PVGS；可主張的是「snapshot completion lock 無法複製 event-driven re-harvest，負結果定位出 slot-binding time 才是主導狀態變數」。

