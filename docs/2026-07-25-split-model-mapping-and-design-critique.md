# 里程碑：split.pdf 三層拆單模型對位 ＋ tier 過度客製化診斷 ＋ Fill 提早釋出

2026-07-25 · branch `fill-early-release` · 承接 `docs/2026-07-21-m2e-ic-current-model.md`

這篇是一個**理解上的里程碑**，不是一個新機制。三件事收斂成一條線：(1) 補完了 Fill 模式對拆單的公平性缺口（EarlyParentRelease，已實作＋驗證）；(2) 用 split.pdf 本文把 tier 在拆單模型光譜上的位置**精確定位**；(3) 據此把 tier 目前的設計做了一次誠實的自我批判，定出下一步的北極星。

一句話：**tier 是 split.pdf「split-over-time」模型的線上受限版；它的拆單優勢是真的（來自拆單能力，不是約束無中生有），但目前用了過多硬約束＋一個扭曲的目標式權重去雕形狀，下一步要把硬約束軟化、並用離線精解當上界量測「最佳解有沒有被我排除」。**

---

## 1. 這次做了什麼：Fill 模式母單提早釋出（EarlyParentRelease）

**問題**：Fill 模式（對齊 online.pdf/split.pdf 原生設定）維持訂單池在 `OrderCount`，補單閘門是「訂單離開 `_availableOrders`」而非「訂單完成」。M1G 整單指派即釋出一名額、吸單快；tier 拆單後母單只在 `IsFullyClaimed` 才釋出，殘餘母單長期佔住名額 → Fill 不補新單 → tier 訂單流被人為壓低。這是 Fill 模式對拆單的結構性不公平。

**修法**：`ReleaseParentOnFirstSplit`（config-gated，預設 false，零漂移）。母單第一次拆分即從 `_availableOrders` 釋出一名額（釋出一次，guard `IsOrderAvailable`），但**仍留在 `_pendingOrders` 跨期服務殘餘**，且**不記為完成**（完成仍只靠 child consolidation）。設計見 `docs/superpowers/specs/2026-07-25-fill-early-parent-release-design.md`。

**驗證（使用者硬性要求：釋出後模型仍看得到並完成）**：
- 零漂移：flag off 時 `kpi_report.csv`/`tripscompleted.csv` 逐位一致，無 `early_release.csv`。
- 正確性：釋出的母單留在 `_pendingOrders`、98.5% 完成，殘餘需求守恆，無孤兒。

**能耗/吞吐讀數（Fill，seed 0/1）**：tier 吞吐仍與 M1G 打平（件數 −1%），但 pile-on +114%、trips −40%、能耗 −46%。提早釋出把 tier 的訂單吸入率拉近 M1G（placed +11 vs off），縮小差距但未補平——殘餘差距＝M1G 暴力 churn 的本質，也正是它多燒能耗的來源。

> ⚠️ **方法論但書**：Fill 兩模型被餵的訂單流不同（M1G churn 快、被生成器餵更多單），對照有混淆。要對外宣稱的數字只能引 **Fixed 同訂單檔**那組（tier 件數 +12%、pile-on 1.74 vs 1.04、每件能耗 −42%）。

---

## 2. split.pdf 三層拆單模型對位（核對本文，決定性）

Xie et al. 2021 (EJOR 288) 疊了**三層**巢狀模型，每層是前一層的超集（Prop 3/5：後者恆優於或等於前者）：

| 模型 | §  | 拆單維度 | 關鍵限制式 |
|---|---|---|---|
| **Integrated**（不拆） | 3.2 | 無：整單→單站 | 一張單所有 order line 綁同站 |
| **Split-among-stations**（跨站） | 3.3 | 跨站、**當期 all-or-nothing** | (11)：若單 active，其所有 line 當期全部指派 |
| **Split-over-time**（跨期） | 3.4 | 跨站 **＋ 跨期部分滿足** | (11.1)：`Σ_s y_ios + y^b_io = y_o`；新變數 `y^b_io`＝「SKU i 退回 backlog」 |

**split.pdf 對「跨期拆單」的原話**：*"some SKUs for an order may be assigned in one period while the others will stay in the backlog to be assigned in later periods."*（部分 SKU 這期指派、其餘留 backlog 等後面期別）。機制＝二元變數 `y^b_io`（退回 backlog）＋限制式 (11.1) 把 (11) 的當期 all-or-nothing 鬆開。

**對位結論**：
- **M1G ＝ Integrated model**（無拆，整單單站）。
- **M2G ＝ Split-over-time**（Xie 最一般版；codebase 的 PVGS/exact 近似之）。
- **tier（M2e-IC）＝ 受限的 Split-over-time**：它做的跨期部分滿足（母單殘餘留 `_pendingOrders`＝Xie 的「stay in backlog for later periods」）**與 split-over-time 語意相同**，但多了 split.pdf 沒有的 **P1（inbound-committed：拆單碎片只能搭已承諾貨架，新車只服整單）**。

**為什麼 split.pdf 不需要 P1**：它是**離線**——預先知道整個 backlog、以 `Min Σx_ps + W_u·Σu_s`（最小化 pod 到站趟次＋空槽罰款）全域求解，拆單是最優解自然長出來的。tier 是**線上**，拿不到全域前瞻；已實證：線上一放掉 P1/T（value-dispatch/nocap），就退化成 M1G churn、pile-on 崩掉。**所以 P1 不是「還沒追上 split.pdf」，是把離線模型搬到線上時、為保住 pile-on 必須補的適應。**

（更正紀錄：本輪之前一度把 tier 從 M2 切開、又一度只認得 split 的跨站版——兩者都經本文核對更正。CLAUDE.md「M1=跨站當期 / M2=跨站跨期部分滿足」的描述經核對**正確**，對應 split-among-stations / split-over-time。）

---

## 3. tier 跨期實證：68% 的拆單是真跨期

用 `early_release.csv` 的 `remainingUnitsAtRelease`（母單第一次拆分當下尚未被任何 child 認領的殘餘件數）分類（Fill，flag-on，seed 0/1）：

| | on_s0 | on_s1 |
|---|---|---|
| 拆單母單總數 | 323 | 327 |
| **真跨期（rem>0，件數延到後面輪次）** | **221 (68%)** | **224 (68%)** |
| 同期拆完（rem=0，只跨站/跨貨架） | 102 (32%) | 103 (32%) |
| 平均每張跨期單延遲件數 | 2.14 | 2.27 |
| 平均 consolidation 等待 | 218 s | 250 s |

**tier 名副其實是跨期拆單模型**：約 2/3 母單把件數留到後面 decode 輪次才認領（split-over-time 的定義行為），是主力不是尾巴。consolidation 等待 218–250s、約 65% 母單首末 child 完成差 >60s，旁證殘餘確實在後面一波被服務。

> 但書：此數字來自 Fill＋flag-on，寫論文前應以 **Fixed 同訂單檔**再確認一次。

---

## 4. 過度客製化診斷（本里程碑的核心）

使用者的疑慮（正確且尖銳）：拆單塑造出的需求形狀（pile-on/能耗優勢）是否被手捏的 P1+T 約束「擠」出來的、甚至**最佳解可能根本不在被捏出的可行域裡**。

### 4.1 兩種不同的失敗，要分開

- **限制式（可行域邊界）**：P1、SG、eshi13'、T=1 —— 會**把解從可行域刪掉**。若最佳解需要「叫新車補拆單碎片」而 P1 禁止它，該最優解就**不在可行域內**。← 使用者真正在怕的東西，只有這類會造成。
- **目標式權重（域內挑點）**：IdleSlotWeight 等 —— 不刪解，只決定挑哪個點。風險是「挑錯點」而非「圈錯地方」。

**推論**：要治這個恐懼，重點是**把硬約束盡量變軟成本**——軟成本不刪任何解，最佳解永遠留在域內、只是被定價。

### 4.2 病灶：`IdleSlotWeight = 1000`（現任 tier 實際值，已核對 xconf）

1000＝M1G 式「每空槽重罰」，把求解器往「填滿每格」＝M1G churn 推。而它是為治 **500-SKU 死鎖**才常駐的，該死鎖真因是**引擎心跳 bug**（事件驅動引擎在配對死角醒不過來，`OrderManager.cs:329` `SituationInvestigated` 只靠事件重設）。

→ **用一個巨大目標式扭曲去蓋一個引擎 bug**，是「不合理設計」的典型。它同時把 pile-on 往 M1G 壓、又讓「tier 吞吐贏是拆單贏還是被 1000 逼填槽贏」分不清。**待辦：查招牌 Fixed「+12%」結果是否在 w3=1000 下跑的——若是，數字與 bug-mask 綁在一起，須拆開才可信。**

### 4.3 放寬 / 收緊 / 保持

| | 項目 | 現況 | 建議 | 理由 |
|---|---|---|---|---|
| **放寬（硬→軟）** | P1（inbound-committed） | 硬禁新車服務碎片 | 折進 pod-tier 成本第 4 層（新車服碎片＝高但**有限**代價） | 軟成本不可能刪解，最佳解永留域內。pod-tier(1,3) 已是 P1 軟版、5-seed 拿 EOR 勝＝軟的有效已證 |
| **放寬** | T（`PipelineFloorTarget=1`） | 每輪派車硬上限=1 | 放到物理車隊上限，或用真實邊際能耗定價 | T=1 是政策數字、遠低於物理；靠人為稀缺撐 pile-on |
| **放寬** | SG 拆單閘門 | 硬布林「Pq 空才准拆」 | 稀缺被 pod-tier 定價後，試拿掉 SG | 布林硬門是「稀缺」的粗代理，定價後多半冗餘 |
| **收緊/清理** | IdleSlotWeight | 1000 | 打回 ~5，死鎖去引擎層加 **watchdog** 修 | 別用目標式扭曲蓋引擎 bug |
| **收緊/清理** | 目標式權重塔 | w2/floor/coverage/tier 多層 | 砍到**兩項誠實的**：吞吐(未滿足需求)＋真實能耗代理(pod-tier 天生正比行程能耗) | 權重越少越物理，「被捏」感越小 |
| **保持硬** | C1(`q≤stock·xps`)、Cs、PK78、車隊數、庫存、C6(滿覆蓋=完成) | 硬 | 維持 | 物理/定義，最優真住裡面＝誠實可行域 |

**設計北極星：硬約束只留物理；其餘全進一個小而誠實的目標式定價；讓求解器自己找點。**

---

## 5. 下一步：用離線精解當上界，量「最佳解有沒有被排除」

言辭安撫無用，要用數字了結使用者的恐懼。**唯一的量測動作**：

> 把離線精解 split-over-time（Xie 完整模型，只有物理約束、無 P1/SG/T）在**同一固定訂單檔**解到最優，取其最優解，檢查它在 tier 的 P1/SG/T 下是否可行。
> - 可行（或幾乎）→ 域**包含**最優，恐懼解除。
> - 不可行 → 看到**是哪條約束刪掉它、刪多少**（＝online regret，可寫進論文的數字）。

而 §4.3 的「硬轉軟」重設計本身也是**診斷器**：改軟後若 pile-on 存活 → 它在誠實能耗定價下就是最優（客製化在揭露真相）；若崩掉 → 它本靠「禁止對手 churn」才存在（客製化在藏非最優）。**兩答案都誠實、都能發表。**

現況提醒：memory 記 PVGS ≥ exact、tier ≤ PVGS（sunk-first-defense 前置「M2e ≥ PVGS」目前反向）——**這個上界 benchmark 現在還沒過**，正是使用者恐懼所戳中的未解洞。

**建議執行順序**：① exact split-over-time 固定訂單檔上界（先知道離最優多遠、有沒有刪到解）→ ② P1 硬門改 pod-tier 第 4 層軟成本，看 pile-on 撐不撐得住 → ③ IdleSlotWeight 打回 ~5＋引擎 watchdog 治死鎖。

---

## 6. 現任 tier 配置快照（`split_milp_m2eic_tier.xconf`，已核對）

```
IdleSlotWeight        = 1000     # ← §4.2 病灶：M1G 式填槽罰款，蓋死鎖 bug
OrderRewardWeight     = -40
PipelineFloorWeight   = 20
PipelineFloorTarget   = 1        # ← T=1，最緊派車上限
CoverageRewardWeight  = -0.2     # tie-break 級
QueuedPodDrawPenalty  = 1        # pod-tier 軟成本 α
OnTheWayPodDrawPenalty= 3        # pod-tier 軟成本 β（即 (1,3)）
```
程式碼裡另有寫死的硬門：P1（icP1d）、SG gate、eshi13'（見 `docs/2026-07-21-m2e-ic-current-model.md`）。
