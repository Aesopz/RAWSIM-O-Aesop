# 現任最強模型：tier（M2e-IC）完整講解

2026-07-25 · `SplitM2eICManager` + `split_milp_m2eic_tier.xconf` · branch `fill-early-release`

這份是「現在最強模型」的**權威、自足**講解。數值全部對齊現行 config（已逐項核對 xconf，2026-07-25）。完整逐條約束式見 `docs/2026-07-19-m2e-ic-close-dispatch-model.md`（close 基礎）與 `docs/2026-07-21-m2e-ic-current-model.md`（pod 分層代價）；本文是**站在拆單模型光譜上、對外可讀**的整合版，並在 §10 誠實標出目前的設計張力。

> ⚠️ 一個必須先講的更正：舊文件寫 `w3(IdleSlotWeight)=5`。**現行 tier 是 `w3=1000`**（2026-07-23 常駐），這改變了模型性格，§7、§10 詳述。

---

## 0. 一句話

> **tier 在每個決策時點解一個混合整數規劃（MILP），用同一個變數 `q[o,i,s,p]`（訂單 o 的 SKU i、幾件、在站 s、從貨架 p 取）一次決定「派哪些貨架去哪站、哪些訂單整單/拆單吃進槽位、每張單的哪些件從哪個貨架撿」——把 RMFS 傳統上分三關（OA 訂單指派 / PS 貨架選擇 / TA 任務分配）接力做的決策，合併成一次聯合最佳化，並允許一張單跨站、跨期分批完成。**

它相對基準 M1G 的賣點不是吞吐量，是**效率**：同等吞吐下 pile-on 翻倍、每件能耗近乎減半。

---

## 1. 定位：它在拆單模型光譜的哪裡

Xie et al. 2021（split.pdf）疊了三層巢狀拆單模型，每層是前一層的超集：

| 模型 | 拆單維度 | codebase 對應 |
|---|---|---|
| **Integrated**（不拆） | 整單 → 單站 | **M1G**（基準） |
| **Split-among-stations** | 跨站、**當期全有全無** | — |
| **Split-over-time** | 跨站 **＋ 跨期部分滿足**（部分 SKU 這期指派、其餘留 backlog 等後面期別） | **M2G / PVGS / exact** |

**tier ＝ 受限的 Split-over-time**：它做的跨期部分滿足（母單殘餘留在 `_pendingOrders` 等後面 decode 輪次＝Xie 的「stay in backlog for later periods」）與 Split-over-time **語意相同**，但多了一條 split.pdf 沒有的規矩——

> **P1（inbound-committed）**：拆單碎片只能從**已承諾、已在動**的貨架抽貨；**新叫的車只服整單**。

**為什麼要多這條**：split.pdf 是**離線**——預先知道整個 backlog、以全域最小化貨架到站趟次求解，拆單優勢是最優解自然長出來的。tier 是**線上**，拿不到全域前瞻；實證顯示線上一旦放掉 P1，就退化成 M1G 式的貨架 churn、pile-on 崩掉（§10）。**所以 P1 是「把離線拆單搬到線上、又要保住 pile-on」的必要適應，不是缺陷——但它也正是目前設計張力的來源（§10）。**

實證上約 **68% 的 tier 拆單是真跨期**（件數延到後面輪次），名副其實。

---

## 2. 核心思想：一個變數同時解 OA + PS + TA

傳統 RMFS 決策鏈是接力的：先 OA（哪張單去哪站）→ PS（補哪個貨架）→ TA（派哪台車）。tier 不接力，用一個 MILP 一次解完，關鍵是那個 unit 級變數：

$$q_{o,i,p,s} \in \mathbb{Z}_{\ge 0} \quad=\quad \text{訂單 } o \text{ 的 SKU } i\text{，從貨架 } p\text{，在站 } s\text{，撿幾件}$$

一個變數同時承載四件事：
- **要不要拆**：同一張單 o 的 `q` 跨多個 `(p,s)` 有值 → 拆單。
- **拆成什麼樣**：件數分到哪個站-part。
- **選哪些貨架**：某貨架 p 有 `q>0` 抽貨，才需要派它（`x_{p,s}=1`）。
- **怎麼填單**：`q` 本身就是「把單填滿」的動作。

**拆單價值直接寫在目標式、不是隱性**：求解器評估要不要派某貨架時，同時看到它的貨能讓某張單湊到完整覆蓋 → 拿完成獎勵。對照 M1G：它的粗配對變數 `dops` 是非題、不數件，求解時**不知道** pod1+pod2 合起來能湊出 order2，得靠求解後的貪婪迴圈事後隨手撿（§9）。

---

## 3. 決策時點（epoch 觸發）

MILP 不定時跑，由事件喚醒：

| 觸發 | 位置 |
|---|---|
| 任一訂單/child 在站完成（槽位釋放） | `OutputStation.cs:373` |
| 新訂單進 backlog | NewOrder 事件 |
| D16：當前 bot 任務只剩 1 個 pick（貨架臨終信號，提前喚醒讓下一批無縫接上） | `OutputStation.cs:258-260, 302-304` |

---

## 4. Pod 狀態四分層（tier 的物理感知）

tier 不把所有貨架當同一回事，而是依「離站台多近」分四類——這是它比 close 基礎版多出來的物理感知，也是 P1 與 pod 分層代價的基礎：

| 集合 | 意義 | 判定 |
|---|---|---|
| $P_p(s)$ | 站 s **正在被撿**的貨架 | bot 的 CurrentWaypoint = 站的揀貨點 |
| $P_q(s)$ | 站 s **排隊中**的貨架 | bot 的 CurrentWaypoint = queue 點（`icQueuedByStation` 記 ID） |
| $P_b$ | **已承諾**（committed）的貨架 | 已被派工、在路上/在站，含本輪剛派出的 |
| $P_a$ | **可用**（available）的貨架 | 還在儲位、本輪可新派的 |

「還在路上」＝ $P_b$ 但既非 $P_p$ 也非 $P_q$，用排除法判定。

---

## 5. 決策變數

| 變數 | 意義 |
|---|---|
| $x_{p,s}\in\{0,1\}$ | 貨架 p → 站 s（PS/TA） |
| $y^R_{b,p}\in\{0,1\}$ | bot b → 貨架 p |
| $q_{o,i,p,s}\in\mathbb{Z}_{\ge0}$ | 訂單/SKU/貨架/站 unit 級取貨（核心，OA + 拆單 + 填單） |
| $y_{o,s}\in\{0,1\}$ | 訂單 o 佔用站 s 一個槽（**逐 (單,站)** → 一單可佔多站槽＝跨站拆單） |
| $z_o\in\{0,1\}$ | 訂單 o 本輪被完整指派（拿完成獎勵） |
| $w_o\in\{0,1\}$ | 整單路徑旗標 |
| $u_s\in\mathbb{Z}_{\ge0}$ | 站 s 空槽數 |
| $e_o$ | 多站-part 超額（每多一站付懲罰） |
| $\sigma_s$ | pipeline 短缺量 |
| $c_i$ | SKU i 的覆蓋量 |

---

## 6. 約束式（分三類看）

完整逐條見 `docs/2026-07-19-...` §5.1-5.9。這裡按「是什麼性質」分三類——這個分類對理解 §10 的批判很重要。

### 6.1 物理／定義硬核（最優解真的住在這裡，永遠保留硬）

- **C1（`q ≤ stock · x`）**：只能從**已派到該站**的貨架抽貨。物理。
- **C6（`Σ_{s,p} q ≥ r · z`）**：一張單算完成，需其需求跨貨架、跨站加總滿覆蓋。定義。
- **Cs（站槽上限）**：每站同時處理的訂單數 ≤ 容量。物理。
- **PK（packing capacity）**：每母單一個打包箱（Xie 附錄 B = 78）；tier 現行**關閉**此約束。
- **車隊數 / 庫存**：`y^R` 受 bot 數限、`q` 受實體庫存限。物理。

### 6.2 拆單機械（讓「拆」這件事語意正確）

- **elink1-3**：把 `q`、`y_{o,s}`、`z_o`、`w_o` 串起來，定義「整單 vs 拆單」路徑。
- **eM1/eM2/eZfin**：完成語意（child 完成 → 母單 consolidation）。
- **D9（`e_o`）**：一張單每多用一個站-part，付 $w_p$ 懲罰（抑制無謂碎裂）。

### 6.3 政策硬門（tier 的線上適應 —— 也是 §10 批判的對象）

- **P1（icP1a-d）**：拆單碎片只能抽 committed（$P_p/P_q/P_b$）貨架；新派 $P_a$ 貨架只服整單（close 例外：收尾母單 `zdone=1` 可抽一次）。
- **SG（icSG1-2）**：新的部分拆單只在「$P_p$ 存在 ∧ $P_q$ 空」才准開（拆單窗口＝後繼貨架的行走窗）。
- **eshi13'**：本輪派出的貨架，當輪必須被消費（派車證成）。
- **D11 / T（icLGcap，`PipelineFloorTarget=1`）**：每輪派車數的人為上限；配 `σ_s` 的 pipeline 地板（releaseLeft ≤ 70s 逼派工）。

> **§10 的核心觀察**：6.1 是誠實的可行域邊界；6.3 是**會把解從可行域刪掉**的政策硬門——若最優解需要「叫新車補一個拆單碎片」，P1 讓它**不可行**。

---

## 7. 目標式（Minimize，九項，現行值）

$$
\begin{aligned}
\min\ \ & w_1\!\Big[\sum_{p\in P_a,s}(d^{PS}_{p,s}+w_4)\,x_{p,s} + \sum_{b,p\in P_a} d^{BP}_{b,p}\,y^R_{b,p}\Big] && \text{(a) 距離＋每趟固定成本} \\
+\ & w_2 \textstyle\sum_o z_o && \text{(b) 完成獎勵} \\
+\ & \mathbf{w_3} \textstyle\sum_s u_s && \text{(c) 空槽壓力} \\
+\ & w_p \textstyle\sum_o e_o && \text{(d) 多部位懲罰} \\
+\ & w_{pipe} \textstyle\sum_s \sigma_s && \text{(e) pipeline 短缺} \\
-\ & w_{2p} \textstyle\sum_{o\in O_{parent}} z_o && \text{(f) parent 收單加碼} \\
+\ & \varepsilon \textstyle\sum \text{scarcity}_i\, q && \text{(g) 稀缺榨取} \\
+\ & \varepsilon_{cov} \textstyle\sum_i c_i && \text{(h) 覆蓋 tie-break} \\
+\ & \textstyle\sum_{o,i,s}\sum_{p\notin P_p(s)} \pi(p,s)\, q_{o,i,p,s} && \text{(i) pod 分層取貨代價}
\end{aligned}
$$

分層代價 $\pi(p,s) = 0$（正在被撿，免費）／ $\alpha$（排隊中）／ $\beta$（還在路上）。

**現行數值（已核對 xconf；粗體＝ config 覆蓋，其餘＝程式碼預設）**：

| 符號 | 項 | 值 | 來源 |
|---|---|---|---|
| $w_1$ | 距離權重 | 1 | 預設 |
| $w_4$ | 每趟固定成本 | 10 | 預設 |
| $w_2$ | 完成獎勵 | **−40** | `OrderRewardWeight` |
| $\mathbf{w_3}$ | **空槽壓力** | **1000** | `IdleSlotWeight` ← **§10 病灶** |
| $w_p$ | 多部位懲罰 | 12 | 預設 |
| $w_{pipe}$ | pipeline 短缺 | **20** | `PipelineFloorWeight` |
| $T$ | pipeline 地板目標 | **1** | `PipelineFloorTarget` |
| $w_{2p}$ | parent 收單加碼 | 20 | 預設 |
| $\varepsilon$ | 稀缺榨取 | −0.5 | 預設 |
| $\varepsilon_{cov}$ | 覆蓋 tie-break | **−0.2** | `CoverageRewardWeight` |
| $\alpha$ | 排隊中每單位 | **1** | `QueuedPodDrawPenalty` |
| $\beta$ | 路上每單位 | **3** | `OnTheWayPodDrawPenalty` |

**(i) 是軟性偏好、不是禁令**：只要完成獎勵夠大（$|w_2|=40 \gg \alpha,\beta$），模型仍可選擇吃排隊中甚至路上貨架的內容；$q=0$ 永遠可行，故此項**不可能造成無解**。這正是 tier 一路踩雷後學到的教訓——軟性代價安全落地、硬規則撞共用引擎（§10 延伸）。

---

## 8. Decode：解 → 派工動作

拿到 MILP 的 $x/y/q/z$ 解後：`x_{p,s}=1` → 對貨架 p 生成到站 s 的搬運請求；`q_{o,i,p,s}` → 生成 extraction 請求（在站 s 從貨架 p 撿訂單 o 的 SKU i）；`z_o=1` 的整單走整單路徑，跨 `(p,s)` 有值的走拆單路徑（`Order.CreateSplitChild` + demand ledger，殘餘留 `_pendingOrders` 跨期）。母單只在所有 child consolidation 完成時才記完成。

---

## 9. 為什麼它是「最強」：實證

### 9.1 決定性證據（Fixed 同訂單檔，可信）

Fill 模式兩模型被餵的訂單流不同、有混淆；唯一能對外宣稱的是 **Fixed 同訂單檔**（`gen_orders.py` 生成、忠實 Mu-500 分布、兩模型逐字相同、deterministic）：

| | tier | M1G | 差 |
|---|---|---|---|
| 完成訂單 | — | — | **+12%** |
| 完成件數 | — | — | **+31%** |
| pile-on | **1.74** | 1.04 | +67% |
| 每件能耗 | — | — | **−42%** |

**同一批訂單、tier 全面勝 M1G。** 這是拆單聯合最佳化的乾淨證據。

### 9.2 機制證據

- **跨期真實性**：68% 的拆單母單把件數延到後面輪次（2 seed 一致），是真 Split-over-time。
- **同吞吐換能耗**（pod 分層代價，4 站/10 bots，5-seed）：TP 打平（雜訊帶內），但 **EOR 五個 seed 全部同向改善 −2.49%**、items 持平——不是犧牲產出換能耗，是零反例的穩定能耗改善。
- **M1G 的吞吐優勢是暴力**：M1G 靠貨架 churn（趟數約 tier 的 1.7 倍）多換到 ~1% 吞吐，代價是近兩倍能耗。tier 用一半能耗、兩倍 pile-on 拿到同等吞吐。

---

### 9.3 為什麼是「獎勵完成」而非「獎勵覆蓋」（2026-07-26 set-cover 實驗）

把拆單看成 set cover（pod visit = 集合、order-line = 元素；split.pdf 自證 NP-hard 由 set cover 歸約）後，一個自然假設是：線上該用「貪婪最大化每趟覆蓋」取代 tier 的完成獎勵。**實測推翻**：`setcover` 臂（覆蓋權重 −5＋軟派車＋w3=5）在 Fixed `orders1150` 上 vs tier＝orders 打平、**pile-on −2.2%、kJ/order +2.0%、trips +2.3%**，全面略差。

機制：set-cover 貪婪假設「蓋到即進度、元素終會蓋完」，**線上不成立**——一張單要全 line 蓋齊＋consolidation 才算完成，蓋一半的單佔槽、還要後續專程補趟。獎勵原始覆蓋 → 把 partial 攤到很多單 → 半開單暴增 → 補單趟增多（trips↑）、每單能耗升（kJ/order↑）、pile-on 降。

**結論（印證 set-cover 框架、同時證成 tier）**：線上有價值的覆蓋是「**能收單的覆蓋**」，不是原始覆蓋。**tier 的完成獎勵 `w2·z_o` 本身就是『完成度加權的覆蓋』＝線上正確的貪婪 set-cover 目標**，不是過度客製化。真正的 completability-discount（partial 價值 × 剩餘可完成性）按此推會收斂回完成獎勵，故不太可能贏 tier。

## 10. 誠實的界線：已知弱點與設計張力

「最強」有前提，口試前必須自己先講清楚：

### 10.1 `w3 = IdleSlotWeight = 1000` —— 惰性安全機制，非病灶（2026-07-26 ablation 更正）

> **更正**：本節原本斷言 w3=1000 是「把 pile-on 往 M1G 壓」的病灶。**2026-07-26 乾淨 ablation 推翻了這個說法**：在 Fixed `orders1150.xorders` 上，只把 w3 從 1000 → 5、其餘不動（`tier_w3=5` 臂），**kJ/order 逐位一致（3.348）、pile-on/orders/trips 全在雜訊帶**。原因：正常場景槽位本就被 T=1 派車上限＋P1 填滿，`Σu_s ≈ 0`，w3 乘上零、是 5 還是 1000 都沒差。

所以 w3=1000 是為 **500-SKU 稀疏死鎖**場景備的**惰性**安全機制（該場景 `Σu_s>0`，w3=1000 逼填槽 → 不死鎖），在正常場景不扭曲 tier、也沒把 pile-on 壓下去。原「用目標式扭曲蓋引擎 bug」的批判**過度**了——它在正常場景根本沒作用；死鎖場景的正解仍是引擎 watchdog，但這不是「蓋 bug 的扭曲」，是兩個獨立場景各自的合理設定。

### 10.2 政策硬門可能把最優解排除在可行域外

P1 / SG / T（§6.3）是**硬約束**，會刪掉解。若真正的最優需要「叫新車補拆單碎片」，P1 讓它不可行——**你無法保證最優解在你捏出的可行域裡**。這是目前最該處理的科學風險。
→ **方向**：把 P1 從硬門改成 **pod 分層代價的第 4 層軟成本**（新車服碎片＝高但有限的代價），軟成本不刪任何解，最優永留域內。§7(i) 的 $(1,3)$ 已證明軟版有效。

### 10.3 相對離線精解，tier 還沒證明自己不輸

現況 memory 記 **PVGS ≥ exact、tier ≤ PVGS**（sunk-first-defense 前置「M2e ≥ PVGS」目前反向）。
→ **唯一能了結的量測**：在同一固定訂單檔上解**離線精解 Split-over-time**（只有物理約束、無 P1/SG/T）當上界，檢查它的最優解在 tier 的 P1/SG/T 下可不可行——可行則域含最優、恐懼解除；不可行則看到是哪條約束刪掉它、刪多少（＝online regret，可寫論文的數字）。

### 10.4 已棄用路線（不要重踩）

scout（排擠）、value-dispatch 單用（pile-on 崩）、slot-honesty（撞 MILP 無解）、defer-allocation（撞引擎 `_assignedOrders`/Ziops 雙閘門）。**共同教訓：任何讓「綁定早於物理到站」的機制，用硬規則都撞共用引擎；只有軟性代價能安全落地。**

---

## 11. 配置快照 + 程式碼地圖

**xconf**（`Material/Instances/CoreBenchmark/small/split_milp_m2eic_tier.xconf`）：
```
IdleSlotWeight        = 1000     # w3，§10.1 病灶
OrderRewardWeight     = -40      # w2 完成獎勵
PipelineFloorWeight   = 20       # w_pipe
PipelineFloorTarget   = 1        # T，最緊派車上限
CoverageRewardWeight  = -0.2     # eps_cov（tie-break 級）
QueuedPodDrawPenalty  = 1        # alpha（pod 分層 (1,3)）
OnTheWayPodDrawPenalty= 3        # beta
```

**程式碼**（`RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`，token `OBSPLITM2EIC`）：
- pod 狀態分層前處理：`icQueuedByStation` 等集合。
- 目標式組裝：`icQueuedPen`/`icOnTheWayPen`（w5 之後、eps 之前）。
- 硬門：P1（icP1a-d）、SG（icSG1-2）、eshi13'、D11（icLGcap）。
- 繼承鏈：`SplitM2eICConfiguration : M2eConfiguration`（間接 `is M1GConfiguration`，走共用派工路徑）。

**保留但預設關閉的 lever**（ablation 用）：`NewPodDispatchByValue`、`SplitCanDriveDispatch`、`AnticipatoryDispatchWeight`、`SplitGateTwilightSec`、`DispatchCapEnabled`、`ReleaseParentOnFirstSplit`（Fill 公平性，本分支新增）。

---

## 12. 延伸閱讀

- `docs/2026-07-19-m2e-ic-close-dispatch-model.md` — close 基礎的完整逐條約束式。
- `docs/2026-07-21-m2e-ic-current-model.md` — pod 分層代價的 as-implemented（注意其 w3=5 已過期）。
- `docs/2026-07-25-split-model-mapping-and-design-critique.md` — split.pdf 三層對位＋本模型的過度客製化診斷（§10 的完整版）。
- `docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md` — M2e-IC v1→v4 設計軌跡。
