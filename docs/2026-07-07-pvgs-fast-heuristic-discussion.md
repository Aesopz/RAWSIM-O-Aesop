# 討論稿：M1e/M2e 的快速求解演算法（PVGS）與 Pod 價值表

日期：2026-07-07
狀態：**討論稿（非 spec）**——供決策「要不要做、怎麼做」，定案後才走 brainstorm→spec→plan 流程。
對應關係：本文之於 SplitM1GExact（M1e/M2e），相當於 HADGS 之於 M1G（online.pdf 的結構）。

---

## 1. 為什麼需要這份文件

論文的方法階梯目前是：

| 層 | 精確法（MILP） | 快速法（heuristic） |
|---|---|---|
| 不拆單（online.pdf 原有） | M1G | HADGS |
| 拆單（本論文新增） | SplitM1GExact（M1e/M2e） | **←這格是空的** |

online.pdf 的敘事骨架是「MILP 給最優解但只能跑小規模；HADGS 以 ~1% gap 的代價把決策時間壓到 0.2s 以下，撐起大規模」。本論文若要沿用同一骨架（而且應該沿用——見 `reference_split_online_paper_style.md`），**拆單側也需要一對 exact/heuristic**。這份文件討論那個 heuristic 長什麼樣子，並發展使用者提出的「pod 價值表」構想。

---

## 2. 數學模型對照：M1G（M0） vs M1e/M2e

### 2.1 一句話版

- **M1G**：決定「哪張單配哪站」（binary），庫存檢查只做站級聚合，pod 歸屬靠求解後貪婪法+反悔機制。
- **M1e/M2e**：決定「哪張單的哪個 SKU 的幾件、從哪個 pod、到哪站」（4D 整數量），庫存逐 pod 焊死，求解即最終答案，無貪婪無反悔。

### 2.2 逐項對照表

| 面向 | M1G（M0） | M1e/M2e（SplitM1GExact） |
|---|---|---|
| **核心決策變數** | `yos/yaos[o,s]`∈{0,1}（整單配站，雙層） | `q[i,o,p,s]`∈Z≥0（SKU×單×pod×站 的件數） |
| **佔槽指示** | `yaos`（兼任決策與佔槽） | `ysp[o,s]`（由 q 經 elink2/elink3 雙向鎖定） |
| **完成指示** | 無獨立變數（yos=1 即整單指派） | M1e：`zfullx[o]`；M2e：`zdonex[o]` |
| **一單一站限制** | shi2：`Σs yos ≤ 1`（硬） | **整條移除**（拆單的定義） |
| **庫存可行性** | shi5：`Σo(需求·yaos) ≤ Σp(庫存·xps)` **站級聚合**——允許冗餘 pod 選擇，需事後反悔 | elink1：`Σo q[i,o,p,s] ≤ stock[p,i]·xps[p,s]` **逐 (SKU,pod,站)**——每件都對應到具體 pod 的具體庫存 |
| **需求側連結** | shi3：`yaos ≤ yos` | elink3：`Σp q ≤ r[o,i]·ysp`（有量必佔槽）+ elink2：`ysp ≤ ΣΣq`（佔槽必有量） |
| **拆單模式** | 不可拆 | M1e：`ΣsΣp q = r·zfullx`（本期全有全無，可跨站）；M2e：`ΣsΣp q ≤ r` + `ΣsΣp q ≥ r·zdonex`（可部分，殘量留 backlog） |
| **目標式獎勵** | w2·Σyos（每指派一張單 −40） | w2·Σzfullx 或 Σzdonex（每**完成**一張單 −40；Spec 2 的 per-unit −40 已棄用，那是 orders-vs-items confound 的來源） |
| **距離成本/槽位懲罰** | w1·(pod-站+bot-pod 距離) + w3·Σus（同） | 完全相同（w1=1, w3=1000） |
| **pod/bot 骨架** | shi6~shi13（pod 一站、繼承鎖定、pod 需 bot、一對一、新 pod 必用） | eshi6~11 逐字保留；shi13 改 eshi13'（直接對 q 求和，dops/shi12 整組移除） |
| **求解後處理** | 貪婪 Ziops 逐 pod 扣帳 + unusedDopsPods 反悔撤銷 | **直接讀 q 落地**（decode→child→Ziops 是格式轉換，非決策） |
| **需求快照** | `order.Positions`（全量） | `order.RemainingPositions`（殘量,跨期帳本） |

### 2.3 規模對比（實測 + 推估）

small 陣地實測（seed0, 7200s, 平均每次決策 pending≈87 單、模型內 pod≈90、2 站）：

| | M1G | M1e（實測） |
|---|---|---|
| q/yos 類變數 | ~174（87×2） | **數千**（87單×1-3SKU×該SKU候選pod數×2站） |
| solveSec 中位數 | <0.01s | 0.045s |
| solveSec p95 / max | — | 0.096s / **4.26s**（3 次 >2s 尖峰） |

**45-bot 大陣地量級推估**（假設：backlog ~300 單、12 站、每 SKU 候選 pod 數十~百級——這些是待驗證的假設，不是實測）：q 變數 ≈ 300×2×50×12 ≈ **36 萬**，elink1/elink3 限制式同步放大。以 MILP 的組合爆炸特性，中位數也許還撐得住，但 tail（small 已見 4.26s）幾乎必然突破線上決策的容忍度（HADGS 論文標準：<3s，實務目標 <0.2s）。**這就是需要 heuristic 的規模論證**——不是猜測 MILP 一定跑不動，而是 tail 風險在小規模已現形。

---

## 3. 核心新做法：Pod 價值表（Residual Coverage Index，RCI）

### 3.1 動機：拆單把決策的自然視角從「訂單」翻轉成「pod」

HADGS 是**訂單中心**的：J 運算子問「這張單能不能被站上 pod 完整滿足」，K(p) 評估「加這個 pod 能多完成幾張單」。這在不可拆的世界是對的——訂單是原子，pod 只是配料。

拆單之後，原子變成「SKU 單位」，而**效益機制是 pile-on**（4o10b 實證：拆單的全部增益來自單趟 pod 服務更多件）。自然視角翻轉成 **pod 中心**：「這個 pod 進站一趟，能替 backlog 消化多少殘餘需求？」這正是使用者 2026-07-04 的直覺（「pod 進站,根據 backlog 抓出需要的 order 馬上滿足」），也正是 Spec 1 heuristic（SplitPlanner）崩潰的反面教材——它按訂單配額裸拆、完全不看 pod 覆蓋，pile-on 掉到 2.5、pod 行程爆三倍。

**Pod 價值表就是把「pod 中心視角」變成可增量維護的資料結構**，讓每次決策不用重算全場。

### 3.2 定義

維護三層：

```
R[i]        全 backlog 對 SKU i 的殘餘需求總量 = Σ_o r[o,i]     （殘量帳本的聚合檢視）
σ[i]        稀缺度 = R[i] / Σ_p avail[p,i]                      （需求/供給比,>1 代表供不應求）
V[p]        pod p 的粗價值 = Σ_{i∈p} min(avail[p,i], R[i]) · (1 + β·σ[i])
```

- `min(avail, R)`：pod 帶再多沒人要的貨也不算價值——只算「賣得掉」的部分。
- 稀缺加權 `σ`：搬運稀缺 SKU 的 pod 更值錢，因為稀缺 SKU 是**訂單完成的瓶頸**（M1e/M2e 的獎勵是 per-order 完成,一張單卡在一個稀缺 SKU 上,其他 SKU 搬再多都拿不到獎勵）。這是把 exact 模型的 completion 獎勵語意，翻譯進 heuristic 的連續近似。

V[p] 是**粗篩用的全域索引**（不含站別、距離）。選 pod 時走兩層：

```
score(p, s) = α1·(完成單數增量 n̈)  +  α1'·(消化件數,稀缺加權)
            − α2·(d(bot*,p) + d(p,s))
            + γ·(關閉既有 split parent 的殘量)          ← M2e 專用,見 §5
```

粗表 V[p] 取 top-K 候選（K≈10~20），只對候選做精算 score(p,s)——**表是索引,精算才是決策**。這跟 HADGS 對每個候選 pod 全量重算 n̈（O(|O|) per candidate）相比，把大部分計算攤提掉了。

### 3.3 增量維護（新穎性所在）

| 事件 | 更新 | 成本 |
|---|---|---|
| 新訂單進 backlog | R[i]+=r → 對 p∈PiSKU[i] 調整 V[p] 的 min 項 | O(含 i 的 pod 數) per SKU |
| 單位被認領（child 建立） | R[i]−= → 同上 | 同上 |
| pod 被揀（JustRegisterItem） | avail[p,i]−= → 單 pod 更新 + σ[i] | O(1)+O(含 i 的 pod 數) |
| 補貨上架 | avail[p,i]+= → 同上 | 同上 |

每個 (p,i) 存目前貢獻值 `c[p,i]`,更新時算 Δ 而非重算整個 V[p]。V[p] 用 lazy max-heap（彈出時驗證,髒了就重插）。**每次決策的粗篩是 O(K log P)，不是 O(P·O)**。

### 3.4 與既有方法的關係（論文定位用）

| 既有物 | 關係 |
|---|---|
| RAWSim-O 的 PPS `Demand` scorer | 「選庫存最被需求的 pod」的一次性計算版；RCI 是它的**殘量感知 + 稀缺加權 + 增量維護**推廣 |
| HADGS 的 K(p)=α1·n̈+α2·d | n̈ 只數「完整完成的單」；RCI 加上 unit 級部分進度（拆單世界的必需）並把重算變增量 |
| Spec 2/3 的殘量帳本（`GetRemainingDemand`） | R[i] 就是帳本的全域聚合檢視——RCI 是帳本之上的索引層,不是新的事實來源（不會有雙帳本漂移問題,V 表壞了可全量重建） |

新穎性主張（謹慎版）：「**殘量感知、稀缺加權、事件驅動增量維護的 pod 價值索引,用於拆單場景的線上 pod 選擇**」。單獨每個成分都有前例,組合與用途（支撐 unit 級拆單的 pod-centric 貪婪）是新的。

---

## 4. 演算法 PVGS-1（對應 M1e：跨站、本期全有全無）

M1e 的難點：**all-or-nothing 是跨站聯合條件**——貪婪法不能「先搬一半再說」,必須在承諾前確認整張單本期湊得齊。

```
每次決策（DecideAboutPendingOrders 觸發）:
  0. 老化預處理: Timestay < 30min 的急單優先（鏡像 M1G Od / HADGS Step 1）
  1. 建站級虛擬庫存 VI[s][i] = Σ_{p∈站s在途pod} avail[p,i] − 已認領
  Phase A（完成導向 OA,零新 pod 成本）:
    for 訂單 o（依 due-time 升冪）:
      if Σ_s VI[·][i] ≥ r[o,i] ∀i 且 涉及站皆有空槽:
        把 r[o,·] 分拆到最少站數（|S|小,貪婪:先試單站,再雙站...）
        commit: 每站建 child、扣 VI、佔槽
  Phase B（pod 增援,價值導向 PS&TA）:
    while 有空槽且 Ra 非空:
      candidates = RCI 粗表 top-K
      for p ∈ candidates, s ∈ 有空槽的站:
        試算: p→s 後,哪些 backlog 單「本期可完整湊齊」（p 的貨+全場 VI）
        score(p,s) = α1·新完成單數 − α2·(d(r*,p)+d(p,s))
      if max score > 門檻: commit (p*,s*,r*): 認領 bot/pod,
        只 commit「完整湊齊」的訂單的 children（all-or-nothing 守恆）,
        p* 剩餘庫存滾入 VI 供後續迭代
      else break
```

**關鍵設計**：Phase B 的試算允許「p 的貨 + 其他站現有 VI」聯合湊單（跨站完成），但**只有全湊齊的訂單才 commit**——沒湊齊的部分認領全部丟棄（相當於 exact 模型 zfullx=0 ⇒ q 全零）。這是用「暫定認領 + 期末回滾」模擬 eM1 等式,成本是每期一個 tentative ledger,不碰真帳本。

## 5. 演算法 PVGS-2（對應 M2e：跨站 + 跨期部分滿足）

M2e 反而簡單（≤ 比 = 好做），但有兩個 exact 模型自帶、heuristic 必須手動補上的防護：

```
每次決策:
  0. 老化預處理（同上）
  A0. 關尾巴優先: 站上在途 pod 若能覆蓋某「既有 split parent」的殘量 → 優先 commit
      （γ 加權;動機:consolidation 尾巴是 M2 吞吐指標的已知拖累,先關舊帳再開新單）
  A.  完成導向 OA（同 PVGS-1 Phase A——完成整張單永遠是每槽位價值最高的用法）
  B.  部分消化（pod-centric milk）:
    while 有空槽且 Ra 非空:
      candidates = RCI top-K
      score(p,s) = α1·完成單數 + α1'·Σ(部分消化件數×σ稀缺權重)
                 − α2·距離 + γ·關閉parent數
      commit (p*,s*): 先 commit 完成單,再 commit 部分 children,
        但部分 child 需滿足: 件數 ≥ θ（反碎拆門檻）且 每單每期最多開 1 個新部分 child
```

**兩道 guardrail 的出處**：
1. **θ 門檻（反碎拆）**：SplitPlanner 崩潰的直接教訓——沒有覆蓋意識的裸拆讓 pod 行程爆炸。exact 模型靠 w3/w2 的全域權衡自動避免病態碎拆；貪婪法沒有全域視野,必須用門檻硬擋。
2. **γ 關尾巴**：M2 的 consolidation 尾巴（2h 窗實測 avg 635s）在 exact 模型裡也存在,但 heuristic 若不加權會更糟（貪婪短視,永遠追新單）。A0 相當於把「拖欠成本」注入目標。

## 6. 兩演算法共有的正確性邊界（從 Spec 3 兩次崩潰學到的）

exact 模型踩過的兩個坑,heuristic 天然免疫或必須顯式處理：

| Spec 3 的坑 | PVGS 對應 |
|---|---|
| elink1 跨訂單超提（多單榨同一 pod） | 貪婪逐件扣 working copy 帳（同舊 Ziops 精神）,**天然免疫**——但 VI/RCI 快取必須跟真帳同步,否則同型 bug 以快取漂移的形式回歸。防護:每期期初從真帳重建 VI,RCI 只當索引不當事實 |
| elink3 缺失（有量不佔槽） | 貪婪 commit 時 child 與槽位同一動作建立,**天然免疫** |

**驗證手段沿用**：small 煙霧 + 對照 exact 的 KPI gap,是抓這類「解得出但物理不對」缺陷的最有效工具（Spec 3 兩個 bug 都是煙霧抓的,單元測試抓不到）。

## 7. 替代方案（考慮過,先不選但值得記錄）

| 方案 | 內容 | 評價 |
|---|---|---|
| **B. Matheuristic（RCI 剪枝的 exact）** | 用 RCI top-K 剪 q 的 pod 維度（每 SKU 只留 K 個候選）,exact 模型照跑 | **最省新程式碼**、保留 MILP 品質;但決策時間仍受 MILP tail 支配,且論文敘事上「剪枝的 MILP」不如「獨立演算法」有貢獻感。**建議當 PVGS 的中間對照組**（三方比較:exact / matheuristic / PVGS,量化「剪枝損失 vs 貪婪損失」——這本身是不錯的實驗章節素材） |
| **C. HADGS + 拆單後處理** | 先跑原版 HADGS（整單指派）,剩餘槽位跑一輪 pod-centric 部分拆 | 最大化重用已驗證程式碼;但 HADGS 的 J 運算子先搶走了「本來該拆的單」的槽位,拆單只撿殘渣,效益上限低。可當 ablation 的另一格,不當主方法 |
| **D. 逐單 set-cover 子問題**（之前討論的 PodAttributionOracle 演化版） | 每單解一個小 MILP 選 pod 組合 | 每單一次 Gurobi call,線上頻率下累積成本高;且失去跨單聯合（兩單共用一 pod 的 pile-on 正是效益來源）。否決 |
| **E. 雙邊匹配/拍賣式** | pod 與 order-part 做 deferred acceptance | 理論漂亮,但收斂輪數不可控,與 RMFS 決策節奏不合;且審查者會問「比貪婪好在哪」而我們答不出。否決 |

## 8. 建議的推進順序

1. **先跑 4o10b 的 M1e/M2e exact**（已排定的下一步）——確認 exact 在拆單效益 regime 的表現與 solve-time tail。若 tail 在 4o10b 已惡化,heuristic 的優先級升高;若 45-bot 推估被實測推翻（MILP 撐得住）,PVGS 從「必需品」降級為「貢獻加分項」,但論文對稱性（表 §1）仍值得做。
2. **PVGS-2 先做**（M2e 對應）:結構簡單、M2e 是效益最好的模式、guardrail 明確。
3. **PVGS-1 後做**:all-or-nothing 的 tentative-commit 機制多一層複雜度。
4. **RCI 表獨立成類**（純邏輯、可單元測試,比照 SplitMilpDecoder/Aggregator 的 TDD 模式）:`R[i]`/`σ[i]`/`V[p]` 的增量更新正確性(事件序列後 == 全量重建)是最該測的不變量。
5. 評估:small 上 PVGS vs exact 量 gap（目標:orders-handled ≥ exact 的 90%,單次決策 <10ms）→ 4o10b → 45-bot。

## 9. 開放問題（定 spec 前要跟使用者確認的）

1. **權重 α1/α1'/α2/γ/β/θ 怎麼定**：沿用 exact 的 w1/w2/w3 換算?還是小規模 grid search?（HADGS 論文用 preliminary experiments 定 α1=−40/α2=1,可仿）
2. **PVGS-1 的 tentative-commit 範圍**：只在單次決策內回滾（建議,簡單）,還是允許跨決策暫存「快湊齊的單」？後者更接近 exact 但引入新狀態。
3. **RCI 是否納入 starvation 感知**：score 加一項「目的站的 EST 迫近度」可把 starve-aware 線索接回來（SlowStartController.Est 現成）——但這會把兩條研究線耦合,建議 v1 不做、留作延伸章節。
4. **命名**：本文暫用 PVGS（Pod-Value Greedy Splitting）/ RCI;論文正式名待定。

---

*本文件的模型對照（§2）可直接改寫進論文的 Model 章;§3-5 是 heuristic 章的骨架;§7 是 ablation 設計素材。*
