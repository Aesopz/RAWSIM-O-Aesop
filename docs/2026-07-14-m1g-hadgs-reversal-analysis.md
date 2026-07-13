# M1G vs HADGS 統治關係「反轉」的結構性分析

日期：2026-07-14
依據：online.pdf（Jiao et al., *Omega* 138 (2026) 103374，本 codebase 的直接原型）逐節對應現有程式碼；本 repo 五組獨立實驗數據。
動機：拆單開放前 M1G（exact）勝 HADGS（heuristic），開放後 PVGS（heuristic）反過來勝 M2e（exact）——「這根本沒有道理」。本文回答：有道理，而且反轉不是拆單造成的。

---

## 0. 結論先行（一段話版）

**M1 家族優化的是「快照的靜態價值」；系統裡另有一塊「易腐的動態價值」——pod 停站窗口內的殘量、bot 時間的機會成本——這塊價值在快照 MILP 裡沒有任何變數能表達（模型沒有時間維度）。HADGS 家族的事件驅動節奏讓它恰好「在價值存在的瞬間」做決策：它不需要建模時機，它的架構本身就是對時機的隱式最優化。** 訂單是原子時（不拆單、站台充裕），動態價值那塊≈0，比的是靜態打包品質，MILP 微勝；bot 稀缺或開放拆單後，動態價值成為主要 margin，事件驅動架構統治。拆單不是反轉的原因——它只是把 heuristic 天生佔優的那個 margin 放大到不可忽視。

---

## 1. 前提校正：反轉「何時」發生（數據）

使用者的前提「拆單前 M1G 統治 HADGS」需要兩個修正。

### 1.1 論文自己的數字：M1G 的統治是毫釐級

online.pdf Table 8（小規模：**2 揀貨站、容量 6、6-12 bots、100 pods**——注意這個 regime）：

| 指標 | M1G | HADGS ΔV |
|---|---|---|
| TP | 310.3-314.5 | **僅落後 0.11-0.32%** |
| RD/OD | — | 落後 0.94-1.35% |
| PO | — | 落後 1.89-2.35% |

HADGS 拿到 M1G 99.7% 的吞吐。對照組 SEQU 落後 12-14%——真正的差距在「聯合 vs 順序」，不在「exact vs greedy」。HADGS 本來就是 M1 目標式的直接近似（K(p)=α₁·n̈+α₂·d 就是式 (1) 的逐 pod 版本，論文 3.3 自己說明），近似得非常好。

### 1.2 我們自己的數據：bot 稀缺時，**不拆單的 HADGS 已經贏 M1G**

4o10b（4 揀貨站、10 bots＝2.5 bots/站——論文沒測過的壓力區間；論文小規模是 3-6 bots/站）：

| 臂 | orders | items | pile-on | 總距離 |
|---|---|---|---|---|
| M1G（seed 0） | 907 | 1988 | **7.74** | **18,070** |
| HADGS（5 seeds） | **969-1041** | 2216-2292 | 6.3-6.9 | 21,092-23,003 |

HADGS 吞吐 **+7~15%**，同時 pile-on 較低、距離較高——它用更多趟數、更多距離換更多完成；M1G 把 bot 時間省成漂亮的距離指標，吞吐卻少了一成。**反轉在拆單之前、在 exact 還握有「原子訂單」主場優勢時就已經發生**，觸發條件是 bots/station 比，不是拆單。

### 1.3 拆單後：TP 反轉幅度其實也只有 ~1%，真正翻倍的是 pile-on

acceptance（2 站 10 bots，同論文 regime）：PVGS-M1e 656 vs M1e-exact 649、PVGS-M2e(θ=6) 648 vs M2e-exact 643（+0.8~1.1%）；同情境 pile-on **8.28 vs 4.60（×1.8）**。TP 被 slot 回收天花板（≈650）壓住，所以效率差距顯示在 pile-on/趟數，不在 TP。

**校正後的正確命題**：統治方向由「哪個資源綁定」決定——travel/打包品質綁定時 exact 微勝；時機/bot 機會成本綁定時 heuristic 勝。拆單與 bot 稀缺是同一個 margin 的兩個放大器。

---

## 2. 兩個架構的設計解剖（論文 ↔ 程式碼逐項對應）

### 2.1 M1（M1G）——每 epoch 一次的快照聯合優化

| 論文 | 程式碼 | 語意 |
|---|---|---|
| 目標式 (14)：min α₁Σx̂ + α₂(Σd·z+Σd·y) + α₃Σu_w | `M1GManager.cs:686-688` `w2=-40, w1=1, w3=1000`（寫死） | **字典序：填滿 slot（1000）≫ 訂單報酬（40）≫ 距離（1）** |
| x̂（可指派）/x（實指派） | `yos`/`yaos`（variableNames[2]/[3]） | 長期目標 f1＝「選中 pod 群能覆蓋的潛在訂單數」 |
| 約束 (4)：Σx = c_w − u_w | `shi4`（M1GManager.cs:713-714） | slot 守恆 |
| 約束 (5)：站級聚合供給 ≥ 需求 | shi5 系列 | **多 pod 聯合覆蓋一張單**——MILP 獨有的「套餐」能力，HADGS 要靠 LSMM 近似 |
| 約束 (7)/(11)：繼承 pod/bot 凍結 | eshi7/eshi11（M2e 同款） | **不可逆承諾**：上一輪押的注本輪不能撤 |
| 觸發：c_w ≥ δ | OrderManager threshold gate | 兩家族共用同一個 trigger（已排除為差異源） |
| 動態轉移 (20)：D(t+1)=F(D(t),S(t),**Γ(t)**) | WHCA* 壅塞、picker 隨機 | **論文自己承認系統轉移含隨機因子 Γ，模型對它無能為力** |

最關鍵的一句話在論文 2.3.2 結尾，值得原文引用：

> "…ensures that the current decision in OJOOPT has a positive influence on subsequent decisions through a long-term objective, **although such influence cannot be accurately quantified**."

f1（潛在完成訂單數）是 value-to-go 的**代理指標**，論文明講它量化不了真正的跨期影響。這個代理在什麼時候好、什麼時候壞，正是整個反轉問題的軸心（見 §3/§4）。

### 2.2 HADGS——事件驅動的交替決策

| 論文 | 程式碼 | 語意 |
|---|---|---|
| Algorithm 1：對每站 OA→（c_w>0）→PS&TA 加**一顆** pod→**goto OA** | HADGSManager.cs 主迴圈 | **一次押一顆、押完立刻重掃**（one-claim-resweep） |
| J 算子（式 21）：P_w 現貨能**完整**滿足才收單 | `AnyRelevantRequests`（HADGSManager.cs:399-403，`All(CountAvailable ≥ demand)`） | **完成閘門 admission**：不收「等貨的單」 |
| G 算子（式 22）：按距離排序取 pod 湊單 | OA 評分 | 同單多 pod 的貪婪近似 |
| K(p)=α₁·n̈+α₂(d_rp+d_pw)（式 23） | PS&TA 評分 | 式 (1) 的逐 pod 邊際版 |
| LSMM | Algorithm 4 對應段 | 多 pod 組合的補洞近似（exact 在這裡本應更強） |

### 2.3 三個結構差異（全部差異收斂於此）

1. **決策粒度**：M1G 一個 epoch 押注「多 pod × 多單 × 多站」的聯合計畫；HADGS 每步押一顆 pod 或一張單，押完用**新鮮狀態**重估。
2. **承諾語意**：M1G 的計畫最優性成立於快照瞬間，執行卻延展數十秒-數分鐘（bot 取 pod、走行、排隊），期間 Γ(t) 使假設衰減，而 eshi7/11 使承諾不可逆——**快照最優 ≠ 軌跡最優**；HADGS 的每步承諾在秒級內生效，衰減窗口趨近零。
3. **Admission 語意**：M1G 的 α₃=1000 把 slot 當**待清庫存**強制填滿（zero-slack）；HADGS 的 J 閘門把 slot 當**選擇權**——現貨湊不滿整單就留空，等到某顆 pod 真的到場、能完成時才行使。空 slot 在動態系統裡有期權價值，快照模型裡它只是被罰 1000 的浪費。

---

## 3. 為什麼在論文的 regime 下 M1G（微）勝

不拆單時，決策物件是**原子訂單**：一張單指派了就必然完成，pod 選了就必然服務它——**計畫的價值不隨時間衰減**。此時：

- Γ(t) 只影響「多久完成」，不影響「會不會完成」→ 快照最優 ≈ 軌跡最優，批次承諾無懲罰；
- 剩餘的 margin 是純組合問題：哪幾顆 pod 湊哪幾張單最省距離、站間怎麼分攤——**這是 MILP 的主場**（約束 (5) 的聯合覆蓋 vs HADGS 的 LSMM 近似）；
- f1 代理（潛在完成訂單數）與真實 value-to-go 對齊良好：能覆蓋的訂單數就是未來會完成的訂單數。

所以 M1G 贏，但只贏在 RD/OD 的 1-1.5%（打包品質的價值上限就這麼大），TP 被站台容量封頂而近乎打平。這就是論文 Table 8 的樣子。

---

## 4. 為什麼 bot 稀缺／拆單後 HADGS 家族統治——同一個理由的兩個放大器

### 4.1 放大器一：bot 稀缺（拆單前就生效，4o10b 數據）

bot 稀缺時，每一趟派車都花掉稀缺資源的機會成本。M1G 的批次計畫每 epoch 押注多顆 pod；α₃ 字典序強推填滿；Γ(t)（WHCA* 壅塞）讓計畫在執行中走樣，而承諾不可逆——**錯押的每一趟都燒掉別的訂單本可用的 bot 時間**。HADGS 一次一顆＋完成閘門＝永遠不為「潛在」派車，只為「當下確定完成」派車。數據指紋完全吻合：M1G 距離低（省travel）吞吐低，HADGS 距離高（多跑）吞吐高——在 bot 稀缺 regime，**「省距離」和「賺吞吐」是對立的**，M1 目標式站在錯的一邊。

### 4.2 放大器二：拆單（開放 unit 級決策）

拆單把決策粒度降到 unit，同時創造了一塊全新 margin：**pod 停站期間的殘量再收割**——其價值壽命是秒級（release 瞬間歸零）。本 repo 的視角重建證明：M2e 與 PVGS 首波揀貨完全相同（打包品質平手！），**pile-on 差距的 100% 來自停站期 re-harvest**（再收割速率相同，差在 pod 停留時長 35s vs 74s）。

PVGS 的 re-harvest 通道正是 HADGS 的 OA-drain 迴圈的拆單版（PVGSManager.cs 類註解自我定位：「positioned as HADGS is to M1G」）：每個完成事件 → 立即以「站上現貨」重掃 backlog → pod 有殘量可用就繼續餵。而 M2e 的快照模型**結構上不可能表達「這顆 pod 90 秒後就走」**——模型裡沒有時間變數，一顆停站 pod 和一顆倉庫深處的 pod 在約束裡只差一個距離係數（而且 Pb 的距離還是沉沒的 0）。

### 4.3 「把動態價值定價進快照」已被系統性證偽（本 repo 五連實驗）

| 實驗 | 假說 | 結果 |
|---|---|---|
| aligned 目標式（w3=0 等） | 獎勵權重沒對齊 | 負 |
| CF coverage-first 兩段解 | 目標式該先保覆蓋再省距離 | 負（629 < 640，greedy 六指標全勝） |
| M2e-PR pro-rata（2026-07-14） | 部分揀取獎勵死區 | 負（死區確實存在且被打破——1/733→30-47 次——但殘量沒降、TP 劑量反應為負） |
| M2e-LB 晚綁定 | 綁定時機太早 | 死於引擎事實（solve 即 pre-claim，晚綁定只延後 3 秒） |
| PR 根因分析 | — | binding constraint＝**slot 空出 × pod 在場的巧合率（15-27%）**，是時機問題不是價格問題 |

三個目標式變體＋一個綁定時機變體全滅，指向同一個結論：**快照模型缺的不是更好的係數，是時間維度本身**。你不能用價格說服一個看不見時鐘的模型去抓住 90 秒的窗口。

（反向的護欄證據：θ=2 partial 氾濫崩到 518、裸拆 heuristic 崩到 780——greedy 也不是天生贏，它贏的前提是**完成閘門**。HADGS/PVGS 的 J-gate 才是「greedy 而不氾濫」的關鍵設計，這與 exact 的失敗合起來構成完整的雙面論證。）

---

## 5. 所以，數學模型應該怎麼設計（回答本次提問）

反轉的理由給出了設計準則：**價值在哪個時間尺度上存在，決策就必須在哪個時間尺度上做出。**

1. **不要再造更大的快照 MILP**。往 epoch 模型加變數/加獎勵/加約束的路已三連負，且有機制級的不可能論證（§4.3）。
2. **正解方向 A——事件粒度的微型 exact**：保留 HADGS/PVGS 的事件節奏（每個事件解一步），把每步的 greedy 評分換成小規模 exact 聯合評分。注意：**PVGS 本質上已經是這個東西**（HADGS 節奏 + 殘量覆蓋值索引），這解釋了它為什麼是目前最強臂。剩餘改良空間是把 K(p) 型評分再精確化（例如單步內的多 pod 組合 exact），預期增益 ≈ 論文裡 LSMM 與 exact 的差距，即 ~1%——小。
3. **正解方向 B——顯式的時機控制（唯一未開發的大 margin）**：release-moment retention。BackfillProbe 已量化：pod 離站瞬間 joint-completion pivotal 佔 14-19%（≈趟數缺口的 41-57%），被 slot 巧合率（15-27%）卡死。在「pod 即將離站」這個事件上做一個微決策（滯留 ≤T 秒等 slot／立即放回），等於**人為製造 slot×pod 的時間巧合**，直接攻擊已被證明的 binding constraint。這是執行層改動（掛點：`BotPutItems`/release 流程，`BackfillProbe.OnExtractRelease` 所在位置），不是 MILP 目標式改動。
4. **論文敘事**：這條分析鏈本身就是論文素材——「exact 每-epoch 聯合優化在原子訂單 regime 的優勢（+1% RD/OD），如何在 unit 級、時機主導的 regime 被事件驅動架構反超（pile-on ×1.8）」，佐以五組消融實驗的系統性排除。online.pdf 4.4 Managerial insights 的寫法（點名與既有文獻認知的衝突）正好承載這個主張：**"a snapshot-optimal joint plan is not a trajectory-optimal policy when decision objects become divisible and their value perishable"**。

---

## 附錄：引用錨點

- online.pdf：§2.3.2（f1/f2 設計動機＋"cannot be accurately quantified"）、式 (14)（α₁/α₂/α₃）、式 (19)（̂O 逾時保護）、式 (20)（Γ(t) 隨機轉移）、§3.1-3.3（Algorithm 1-3、J/G/K）、Table 5/6（2 站/cap6/6-12 bots）、Table 8（ΔV 0.11-0.32%）。
- 程式碼：`M1GManager.cs:686-688`（w1/w2/w3 寫死＝α₂/α₁/α₃）、`M1GManager.cs:713-714`（shi4＝式(4)）、`HADGSManager.cs:399-403`（AnyRelevantRequests＝J 算子）、`PVGSManager.cs:1-8`（類註解：positioned as HADGS is to M1G）、`SplitM1GExactManager.cs:496-505`（eshi7/11 繼承凍結）、`:570-576`（solve 即 pre-claim）。
- 本 repo 數據：output_4o10b_m1g vs output_4o10b_hadgs_aligned_s0-4（拆單前反轉）；output_acc_*（拆單後 TP/pile-on）；docs/2026-07-12-po-gap-diagnosis.md（視角重建）；.superpowers/sdd/progress.md M2e-PR 段（PR 三 Gate 數據）。
