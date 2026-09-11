# 論文總體 Spec：能耗感知的線上拆單聯合優化（Energy-Aware Online Order Splitting in RMFS）

日期：2026-07-10
性質：**研究敘事 spec**——定案論文的完整故事線、方法體系、實驗證明計畫與貢獻主張。
實作類 spec（Spec 1 enabler / Spec 2 SplitM1G / Spec 3 SplitM1GExact）為本文件的下游構件；
PVGS 討論稿（`docs/2026-07-07-pvgs-fast-heuristic-discussion.md`）與模型拆解
（`docs/2026-07-09-splitm1g-exact-model-explained.md`）為配套素材。

---

## 1. 文獻血緣與定位：兩篇源頭論文各給了什麼、缺了什麼

本研究是以下兩篇論文的**直接延伸**（並且實作平台 EE-RAWSim-O 與 online.pdf 同源）：

> **[Xie2021]** Xie, L., Thieme, N., Krenzler, R., & Li, H. (2021). Introducing split
> orders and optimizing operational policies in robotic mobile fulfillment systems.
> *European Journal of Operational Research*, 288(1), 80–97.

> **[Jiao2026]** Jiao, G., Huang, M., Song, Y., Li, H., & Wang, X. (2026). Online
> joint optimization of order picking process in robotic mobile fulfillment systems.
> *Omega*, 138, 103374.

| 能力維度 | [Xie2021] | [Jiao2026] | **本研究** |
|---|---|---|---|
| 拆單（cross-station / cross-time） | ✅ 首創（split-among-stations / split-over-time） | ❌ | ✅ 繼承並深化 |
| 線上聯合 OA+PS+TA | ❌（POA+PPS，TA 未入模） | ✅ 首創（OJOOPT / M1） | ✅ 繼承 |
| pod 級精確歸屬（unit→pod 在模型內決定） | ❌（模型只到 station 層） | ❌（同左） | ✅ **本研究新增**（M1e/M2e，4D `q[i,o,p,s]`） |
| 能耗指標與能耗感知決策 | ❌（KPI 僅 PSV/距離/pile-on/吞吐） | ❌（KPI 僅 TP/RD/OD/PO） | ✅ **本研究新增**（EOR 指標 + λ 能耗係數） |
| 大規模可擴展 heuristic | ✅（top-n 訂單預篩） | ✅（HADGS） | ✅ **PVGS**（HADGS 的拆單世界對應物） |
| 逾期/服務品質守門 | ❌（無 deadline 實驗） | △（τ 老化機制,無逾期報告） | ✅（OrdersLate/lateness 全程監測） |

**一句話定位**：[Xie2021] 證明了「拆單能省 pod 行程」，[Jiao2026] 證明了「OA/PS/TA 要聯合、要線上」；
本研究把兩者合流（**線上聯合優化的拆單**），再往下鑽兩層它們都沒碰的維度——
**pod 級精確歸屬**與**能耗**——並補上大規模化的 heuristic 對應物。

引用姿態：不是「重新實作」兩篇論文，而是**站在 [Jiao2026] 的 EE-RAWSim-O 程式碼基礎上**
（M1G/HADGS 命名同源），把 [Xie2021] 的拆單概念移植進 [Jiao2026] 的線上聯合框架，
消融基準 M0 即 [Jiao2026] 的 M1G 原件（一行未改，作為 ablation 對照的硬保證）。

---

## 2. 研究問題（RQ）

- **RQ1（拆單×線上聯合）**：在 bot 稀缺、station 充裕的壓力情境下，unit 級線上拆單聯合優化
  能否顯著提升吞吐與 pile-on？效益的 regime 邊界在哪裡（bots/station 比）？
- **RQ2（粒度階梯）**：pod 級精確歸屬（M1e/M2e）相對 station 級拆單＋貪婪歸屬（M1/M2）
  多買到什麼？（訂單數 confound 修復、反悔機制消除、解=工單的落地語意）
- **RQ3（能耗）**：拆單對單位訂單能耗（EOR）的一階效應（pile-on→趟次下降）有多大？
  能耗感知成本係數（λ）能否在吞吐幾乎無損下收割二階節能？吞吐-能耗 Pareto 前緣長什麼樣？
- **RQ4（可擴展性）**：PVGS 能否以次毫秒級決策時間保住 exact 模型的大部分效益，
  使 45-bot 級大規模模擬可行？（對應 [Jiao2026] 中 HADGS 之於 M1G 的角色）

---

## 3. 故事線（論文敘事弧，五幕）

**第一幕｜Gap**：電商倉儲能耗日益受關注，能耗感知的 RMFS 文獻存在（如 Li et al. 2020
的 energy-aware storage assignment——[Jiao2026] 文獻表列有此線），但 [Xie2021] 與
[Jiao2026] 的 KPI 體系均**無任何能耗維度**；同時 [Xie2021] 的拆單是離線的 POA+PPS、
[Jiao2026] 的線上聯合不拆單——「線上聯合 × 拆單 × 能耗」三格交集為空。

**第二幕｜拆單的吞吐效益與其正確打開方式（RQ1）**：
以 M0（=[Jiao2026] M1G 原件）為基準,证明:
(a) 拆單效益存在明確 regime——2站/20bots 站台飽和時三組打平,4站/10bots（bot 稀缺）
時 items +27~48%、pile-on +30~40%（已有實證）;壓力軸是 **bots/station 比**。
(b) 裸拆會崩潰——無聯合優化的 heuristic 拆單（H）pile-on 暴跌至 2.5、pod 行程×3
（已有實證）——**拆單必須與 PS/TA 聯合決策**,這是把 [Xie2021]（拆單）接上
[Jiao2026]（聯合）的必然性論證。
(c) 長時窗下 M0 自我退化（8h: pile-on 3.46→2.74、TP 453→374）而拆單模型維持——
拆單「不只提升穩態,還防退化」（已有實證）。

**第三幕｜粒度階梯：從 station 級到 pod 級（RQ2）**：
M1/M2（`q[i,o,s]`＋求解後貪婪歸屬）已較 M0 提升 items,但 per-unit 獎勵造成
訂單數 confound（m1=565 vs M0=654）;M1e/M2e（`q[i,o,p,s]`＋per-order 完成獎勵）
把 pod 歸屬收進模型:訂單數追平 M0（649 vs 654）、pile-on +13%、逾期回到 M0 水準
（已有實證,small seed0）。並以兩個煙霧實證的反例（庫存跨單超提、槽位超收）論證
「聚合模型的鬆散不是理論潔癖」——這幕同時回應 [Xie2021]/[Jiao2026] 模型都停在
station 層的殘留缺口。

**第四幕｜能耗：從副產品到設計目標（RQ3）**：
(a) 物理分解:Rizqi 能耗模型下每趟 ≈（215kg 常數＋載貨）×距離,常數主導 ⇒
**一階節能槓桿=趟次（pile-on）**,拆單已自動兌現（EOR −18%、E/item −25%,已有實證）。
(b) 自由度論證:unit 級拆單使「5件A」可由多 pod 組合供給,吞吐最優解的等值集合擴大;
把成本係數從距離換成能耗代理（λ 混合）,即「**在吞吐最優面內挑能耗最低點**」——
不是拿吞吐換能耗,是收割拆單創造的鬆弛（slack harvesting）。
(c) λ 掃描畫出吞吐-EOR Pareto 前緣,管理者用單一軟體參數選營運點。
Managerial insight（比照 [Jiao2026] §4.4 的體例,並與其「减少 pod 移動未必提升效率」
的洞見對話）:**拆單同時把倉庫「加速」與「降耗」,且兩者的交換率可調**。

**第五幕｜規模化（RQ4）**：
exact 模型 small 實測中位 45ms 但 max 4.26s,45-bot 推估 q~10⁵ 級變數,tail 不可接受;
PVGS（prize-collecting CFLP 定式＋q 層可分解定理＋次模/CELF 價值表＋流量 oracle）
以次毫秒決策、結構性不可產生不可行解的性質,對應 [Jiao2026] 中 HADGS 的角色,
完成方法階梯的最後一格。品質以 small/4o10b 上 vs exact 的 gap 量化
（比照 [Jiao2026] Table 8 的 ΔV 體例,HADGS 的先例 gap ≈1%）。

**章節映射**：第一幕→Introduction+Literature；第二三幕→Model 章（M0→M1/M2→M1e/M2e
遞進,各附白話+數學式,素材=既有三份模型文件）+Experiments 前半;第四幕→能耗章
（方法半頁+實驗一節）;第五幕→Heuristic 章（PVGS）+大規模實驗;Managerial insights
獨立小節收尾（[Jiao2026] 體例）。

---

## 4. 方法體系總覽（已完成 vs 待做）

| 構件 | 狀態 | 出處 |
|---|---|---|
| M0 = M1G 原件（不動,消融基準） | ✅ | [Jiao2026] / 本 repo 原有 |
| Spec 1：Child-Order 資料模型 + 殘量帳本 + consolidation | ✅ 完成 | branch 6/24 |
| Spec 2：SplitM1G（M1/M2,`q[i,o,s]`） | ✅ 完成 | 同上 |
| Spec 3：SplitM1GExact（M1e/M2e,`q[i,o,p,s]`） | ✅ 完成（READY TO MERGE） | 同上 |
| 逾期統計輸出（OrdersLate/lateness/KPI_LATE） | ✅ 完成 | bfe4888 |
| EOR/E-per-item 指標（機械能,Rizqi） | ✅ 既有 | InstanceStatistics KPI block |
| **能耗感知係數（λ）** | ⬜ 待做（本 spec §5） | — |
| **PVGS（含 RCI 價值表、流量 oracle、α₄ 親和項）** | ⬜ 待做（獨立 spec） | 討論稿已備 |

## 5. 能耗感知設計（λ 內化,下一個實作 spec 的核心需求）

**原則：改係數,不改結構。** M1e/M2e 的目標式成本項由距離換成能耗代理:

```
c^E(p,s) = (M_robot + M_frame + CapacityInUse(p)) · d(p,s) + κ_visit   （載重行,κ_visit 涵蓋轉向/升降的 per-trip 常數）
c^E(r,p) = M_robot · d(r,p)                                            （空車行）
c(·) = (1−λ)·d̃(·) + λ·Ẽ(·)                                            （d̃/Ẽ 各自正規化後線性混合）
```

- 零新變數、零新約束;`q`/elink/eshi 全部不動。
- config：`SplitM1GExactConfiguration.EnergyCostLambda`（double,default **0**）——
  default 0 保證所有既有數字 bit-identical,消融安全。
- PVGS 側同構:評分函數的距離項換 `Ẽ`,共用同一 λ 語意。
- `CapacityInUse` 取決策快照當下值（與 elink1 的庫存快照同時點,一致性免費）。

## 6. 實驗計畫（證明矩陣）

**共用設定**：EE-RAWSim-O,x64 Release,WHCA*n,HADGS 家族標準 xconf 慣例;
**每組合 ≥5 seeds**（比照 [Xie2021]/[Jiao2026] 的 10 runs 慣例,現有單 seed 結果全部要補);
報 mean±CI,主張差異處做配對比較。KPI 全家桶:TP、OrderPileOn、ItemPileOn、RD/OD、
**EOR、E/item**（機械能）、OrdersLate/AvgLateness、consolidation 統計（splitorders.csv）、
solveSec 分佈（decision log）。

| 編號 | 實驗 | 配置 | 驗證的假說 / RQ | 成功判準 |
|---|---|---|---|---|
| **E1** | 消融階梯（標準 small） | small × {M0,M1,M2,M1e,M2e,H} × 5 seeds × 7200s | RQ1/RQ2 基本盤;M1e≈M0 訂單數且 PO↑ 的 confound 修復主張 | M1e 訂單數與 M0 差距 <2%,PO 顯著較高;H 崩潰重現 |
| **E2** | 拆單 regime（主戰場） | 4o10b × 同上六組 × 5-10 seeds × 7200s | RQ1:bot 稀缺下拆單效益;RQ2:M1e/M2e vs M1/M2 增量 | items/PO 增益重現且過統計檢定;M1e/M2e ≥ M1/M2 |
| **E3** | 長時窗防退化 | 4o10b × {M0,M1e,M2e} × 3 seeds × 28800s | 第二幕(c):M0 退化、拆單防退化,M2e consolidation 尾巴轉正 | M0 的 PO/TP 隨時間下滑而 M1e/M2e 持平;M2e-M1e 訂單差距縮小 |
| **E4** | **能耗 Pareto（本研究招牌）** | 4o10b × {M1e,M2e} × λ∈{0,.25,.5,.75,1}（M0 為參考點） × 5 seeds | RQ3:一階（λ=0 vs M0 的 EOR 差）+二階（λ>0 的額外節能 vs 吞吐損失） | 畫出前緣;存在 λ 區間:EOR 額外下降且 TP 損失 <2%;全鏈能耗（含補貨側）不倒賠 |
| **E5** | 逾期守門 | E2/E4 全部組合順帶回報 | 拆單/能耗偏好不製造逾期災難 | OrdersLate 率 ≤ M0 同量級（現有證據:M1e late=2 = M0） |
| **E6** | 規模與 heuristic | (a) 45-bot large × M1e/M2e:solveSec tail 實測;(b) small/4o10b × {exact, PVGS}:gap 表;(c) large × {M0,H,PVGS(λ=0),PVGS(λ*)} | RQ4 | PVGS gap ≤10%（目標 ≈HADGS 的 1% 量級）;決策時間 <10ms;large 上 PVGS 重現拆單+節能效益 |
| **E7**（選配） | PVGS 內部消融 | PVGS ± α₄ 親和項 ± θ/γ guardrails | 軟共分群與 guardrail 的各自貢獻 | 逐項增益可歸因 |

**執行順序**：E2（含 E1 併跑）→ E4 → E3 → E6(a) → PVGS 實作 → E6(b)(c) → E7。
E6(a) 的 tail 數據決定 PVGS 敘事強度（「必需品」或「加分項」），故前置。

## 7. 貢獻清單（每條綁定源論文的空格）

1. **線上拆單聯合優化模型家族**（M1/M2→M1e/M2e）:把 [Xie2021] 的拆單從離線 POA+PPS
   移植進 [Jiao2026] 的線上 OJOOPT 框架,並首次把 pod 級歸屬收進模型
   （4D `q`,prize-collecting capacitated facility location 定式,NP-hard 性質由
   兩篇源論文的既有證明直接繼承）。
2. **regime 發現**:拆單效益的壓力軸是 bots/station 比;長時窗下拆單防止基準系統自我退化
   ——兩者皆為 [Xie2021] 未觸及的動態性質。
3. **能耗維度**:EOR/E-per-item 指標補上兩篇源論文 KPI 體系的空格;
   「pile-on=一階節能、λ 係數=二階收割」的機制分解與吞吐-能耗 Pareto 前緣
   ——RMFS 拆單文獻首次連接能耗。
4. **PVGS**:拆單世界的 HADGS 對應物——q 層可分解結構定理、次模/CELF 化的 pod 價值表、
   流量 oracle 的 by-construction 可行性;附 vs exact 的 gap 量化。
5. **方法論副產品**（誠實記錄）:聚合約束精確化的兩個實證陷阱（跨單庫存超提、
   有量不佔槽）,對「從 station 級模型細化到 pod 級」的後繼研究是可移植的 checklist。

## 8. 風險與誠實限制（先寫進論文,別讓委員先問）

- **輕 pod 掏空回饋**（E4）:偏好輕 pod 加速其枯竭→SKU 覆蓋下降、補貨行程增加;
  能耗帳必須含 input 側,預期 λ 過大時前緣回彎——這是實驗設計的一部分而非缺陷。
- **二階節能效果量未知**:常數質量主導 ⇒ λ 增量可能僅個位數百分比;主菜是一階
  （已實證 −18~25%）,二階以「免費」為賣點,量小照實報。
- **`zdonex` 語意**:「模型可見殘餘 SKU 全出清」≠「訂單字面完成」（objective-only,
  KPI 不受影響）——定義照實寫。
- **M2e consolidation 尾巴**:固定觀察窗下壓低 orders-handled（已證 8h 窗縮小差距）,
  報告時窗敏感性而非單點。
- **統計檢定力**:現有全部正向結果為單 seed;E1-E6 的多 seed 是主張成立的必要條件,
  效果量（+15~48%）遠大於 WHCA* 噪音（~2%）,預期 5 seeds 足夠。
- **solve-time tail**:small 已見 4.26s 尖峰;45-bot 的 tail 實測（E6a）決定「exact 只跑
  中小規模、large 交給 PVGS」的分工敘事——這與 [Jiao2026] 對 M1G/HADGS 的分工完全同構,
  有先例可循。

## 9. 與兩篇源論文的引用對照（寫作時的 checklist）

| 論文位置 | 引用什麼 |
|---|---|
| Introduction | [Xie2021]:拆單首創、packing/consolidation 假設、NP-hard;[Jiao2026]:OJOOPT 定義、線上性論證（Kiva 即時性）、EE-RAWSim-O 平台 |
| Literature | 兩篇的文獻表為底,加 energy-aware RMFS 線（Li et al. 2020 等,[Jiao2026] 表內已有）與「三格交集為空」論證 |
| Model | M0 全套約束=[Jiao2026] M1;拆單模式 M1/M2 的語意=[Xie2021] split-among-stations/split-over-time 的線上化;槽位制 vs item-capacity 的取捨（[Xie2021] 用 item-capacity,本文用 slot 制,理由:與引擎一致,§Spec2 決策 1） |
| Heuristic | PVGS 對標 [Jiao2026] HADGS（交替貪婪→價值表貪婪的推廣）與 [Xie2021] 的 top-n 預篩（被 RCI 粗篩取代） |
| Experiments | 指標定義沿用 [Jiao2026] §4.1.4 三段式;10-run 慣例;sensitivity 的「兩力拉扯→轉折點」敘事模板;Managerial insights 體例 |

---

**下游待辦**（依序）:① E1/E2 多 seed 批次（含 M1e/M2e 首次上 4o10b）→ ② λ 能耗係數
實作 spec（小,一張價目表+一個 config 欄位+回歸保證）→ ③ E4 → ④ E6(a) → ⑤ PVGS 實作
spec（brainstorm 已備料:RCI/流量 oracle/α₄/θ/γ 與四個開放問題待拍板）。
