# M2e-IC「set-level 派遣」重設計

2026-07-27 · 作者 Aesop + Claude · 前置分支 `fill-early-release`（建議另開分支獨立探索）

## 1. 動機與問題

現任 tier（`SplitM2eICManager` + `split_milp_m2eic_tier.xconf`，commit `e3d842f`）在 Fill 模式放大 backlog（OrderCount 200/300）或稀疏庫存時**會死鎖**：某時刻沒有任何單一貨架能在某站獨力完成一張整單（`icWhole=0`）→ 派遣被封死 → 艦隊全閒 → 事件驅動引擎無自我喚醒 → 凍死。實測 o200 凍@5624s、o300 凍@1981s（tier 全程健康的是 o100）。

**根因（使用者主導診斷，已用決策日誌坐實）**：tier 的派遣正當性是**單 pod 級**的——`icP1d` 要求「動用新車的單必須能整單完成（`w_o`）」。低庫存時一個 pod 只帶得動一張單的少數 SKU，沒有任何單能被某新車整單完成 → `icWhole=0` → `icP1d` 強制 `Σq_Pa=0` → `eshi13'` 禁派空車 → `xps=0` → 凍。**M1G 免疫**是因為它是**設級（set-level）評估**：值的是「派往站 s 的 pod 集合合起來能完成幾張單」，不要求任何單一 pod 獨力完成，也沒有 P1。

**連帶問題**：
- **whole-vs-split 全域權衡不完整**（3A2B 例）：一張 3A2B 單可「A pod 整單」或「拆成 B、C pod」，後者也許利於其他單完成。現況聯合 MILP 只在「共享利益＝本輪就完成其他單、且用 committed pod」的窄條件下才完整權衡；部分推進（跨期）、需新車的拆法、未來訂單這三種共享利益都被低估或被 P1 封死。
- **權重塔有概念重複**：cov（SKU 級覆蓋 `ε_cov·c_i`）與 w2（完成獎勵 `w2·z_o`）其實是同一概念（「這批 pod 能服務多少訂單」）的兩端。

**本重設計目標**：把派遣正當性從 per-pod 硬閘搬到 **set-level「完成數 + 推進度」目標**，一併解決死鎖泛用、whole-vs-split 全域權衡、權重塔接地——**但不能讓 tier 收斂成 M1G（pile-on 崩）**。

## 2. 設計決策（brainstorm 定案）

| 決策 | 選定 | 理由 |
|---|---|---|
| **P1 命運** | **軟化成成本**（非完全移除、非保留+旁路） | 死鎖從此不可能（永遠有可行派遣、只是貴），pile-on 來源改由目標式定價保住。符合「把塑形從約束挪到目標式」原則。 |
| **推進度度量** | **訂單級線性**：`Σ_o (o 本輪服務單位 / o 原始總需求)`，小權重 | 最簡、可解釋、死角時非零（能破死鎖）。靠 w2 主導 + γ 成本防碎裂；若碎裂再升級到完成度加權。 |
| **pile-on 承重** | **pod 分層代價加第 4 層 γ** | 「偏好沉沒供給」從硬規矩變遞增軟成本；統一、重用既有 α/β 機制、可解釋（協調/ETA 不確定性遞增）。 |

**定案子問題**：
- **推進項分母 = 原始總需求 $D_o$**（非剩餘需求）：反映真實完成度；一張 parent 一生各輪的比例分加總 = 1.0，與整單一致，且不會過度獎勵近完成殘餘（那由 w2 完成獎勵負責）。
- **γ = 可掃參數**，預設 0（off），重設計臂起手值 $\gamma \ge \beta$（例 6，= 2β），驗證時掃力道。
- **訂單級推進項取代 SKU 級 cov**：重設計臂設 `CoverageRewardWeight`（$\varepsilon_{cov}$）= 0，改用新的 $\varepsilon_{prog}$——順帶完成「w2/cov 統一」。

## 3. 數學模型（相對現任 tier 的 delta）

沿用 `docs/2026-07-19-m2e-ic-close-dispatch-model.md` 與 `docs/2026-07-21-...` 的全部集合/參數/變數/約束。以下只列**改動**。現任 tier 已無 w4（PodTripFixedCost=0）、無 w2p（ParentClosingReward=0）。

### 3.1 新增參數

| 符號 | 意義 | config 欄位 | 預設 |
|---|---|---|---|
| $\varepsilon_{prog}$ | 訂單級線性推進獎勵權重（負=獎勵） | `ProgressRewardWeight` | 0 |
| $\gamma$ | 新車（$P_a$）服 partial 的分層代價（第 4 層） | `NewPodPartialPenalty` | 0 |
| — | 軟化 inbound-committed 硬閘的總開關 | `SoftInboundCommitted` | false |
| $D_o$ | 訂單 $o$ 的**原始**總需求 $\sum_i \text{GetDemandCount}(o,i)$（常數） | — | — |

### 3.2 目標式改動

> **實作硬約束**：以下兩項目標式改動、§3.3 三條約束移除、§3.4 decode 斷言，**全部由 `SoftInboundCommitted` 主開關閘控**——關閉時整段程式碼**不執行**（不是靠「權重=0」讓項變零）。這是 zero-drift 的實作保證：本 session 的 PK 教訓證明「不 binding 的項也會擾動退化面 tie-break」，故必須整段跳過，不能靠係數歸零。`ProgressRewardWeight`/`NewPodPartialPenalty` 只在主開關開時才有意義。

**(新增) 訂單級線性推進項**（主開關開時才生成）：
$$+\ \varepsilon_{prog} \sum_{o \in O} \frac{1}{D_o} \sum_{i,p,s} q_{o,i,p,s}$$
每張單本輪被服務的單位數，除以其原始總需求 = 本輪推進比例。完成的單這項≈1.0（外加 w2 完成獎勵）；未完成的拿比例分。$|\varepsilon_{prog}| \ll |w_2|$ 確保完成主導、推進只是「些微」次要偏好。

**(修改) pod 分層取貨代價（原 (i) 項）加第 4 層**（$P_a$ 項僅主開關開時併入求和；關閉時求和範圍與現任 tier 一致）：
$$\pi(p,s) = \begin{cases} 0 & p \in P_p(s)\ \text{（處理中，免費）} \\ \alpha & p \in P_q(s)\ \text{（排隊中）} \\ \beta & p \in P_b \setminus (P_p \cup P_q)\ \text{（在途）} \\ \gamma & p \in P_a\ \text{（全新派車，}\gamma \ge \beta\text{）} \end{cases}$$
求和範圍從「$p \notin P_p(s)$」擴到含 $P_a$。原本 $P_a$ 不入此項是因為 P1 禁止新車服 partial；軟化後新車可服 partial，須被 $\gamma$ 定價。

**(關閉) SKU 級覆蓋項**：重設計臂 $\varepsilon_{cov} = 0$（由訂單級推進項取代）。

w2 完成獎勵、距離、w3、wp、w_pipe、ε 稀缺榨取**不變**。

### 3.3 約束改動（`SoftInboundCommitted = true` 時）

**不生成**以下三條硬閘：
- `icP1d`（`Σq_Pa[o] ≤ M·gate` 的 Pa-draw 整單閘）→ 新車可服 partial（付 γ）。
- `icSG1`（閘門關時 `y_{o,s} ≤ z_o`）→ fresh 單不再被時機閘門逼完成。
- `icSG2`（fresh partial 只准抽處理中 pod）→ fresh partial 可抽任意 pod（付分層代價）。

**保留不動**：
- `icP1a/b/c`（定義 `w_o` 整單旗標，decode 的 whole/split 路徑仍需要它）。
- `eshi13'`（`x_{p,s} ≤ Σq`，派車須被消費）——partial 服務即滿足（有 q>0），不需改；這也擋掉「派全空車」。
- `elink1-3`、`eM2/eM2done`、`eshi4/6-10`、`icD9`、`icLGcap/icLG1`、C1/C6/Cs/PK 等物理與定義約束**全留**。

### 3.4 Decode 改動（`SoftInboundCommitted = true` 時）

- **跳過 decode 的 P1 斷言**（現況 decode 對拆單斷言 `Q^{Pa}_o = 0`，close 例外）——軟化後 Pa partial draws 合法，此斷言必須 gated off，否則會誤觸崩潰。
- 其餘 decode（新行程、fastPath、CreateSplitChild、consolidation）**不變**。

## 4. 架構決策

- **全部加成 `SplitM2eICManager` 的 config-gated flag**，不開新 manager 檔。理由：本 session 所有 lever（SplitCanDriveDispatch、NewPodDispatchByValue…）都是這樣做的；改動是加法 + gated；**現任 tier 靠三 flag 預設關達成逐位不變**。憲法「勿動 M1GManager/HADGSManager」不涉及 IC 檔。
- **`SoftInboundCommitted` 是主開關**：關閉 = 現任 tier bit-identical（閘控全部五處改動——目標式兩項、約束三條、decode 斷言——整段跳過，不靠權重歸零）。`ProgressRewardWeight`（$\varepsilon_{prog}$）、`NewPodPartialPenalty`（$\gamma$）是主開關開時才作用的可掃權重。zero-drift 為硬驗收。
- 新增 config 欄位到 `MethodConfigurationsOB.cs` 的 `SplitM2eICConfiguration`。新 xconf 臂 `split_milp_m2eic_setlevel.xconf`（tier 副本 + `SoftInboundCommitted=true` + `ProgressRewardWeight=<swept>` + `NewPodPartialPenalty=<swept>` + `CoverageRewardWeight=0`）。

## 5. 風險

- **R1 partial 氾濫**（memory θ=2 崩至 518 重演）：推進獎勵誘發大量半開單。緩解＝$\varepsilon_{prog}$ 小 + $\gamma$ 壓新車服 partial + w2 主導；**須掃出安全區、不是開關式崩盤才算過**。
- **R2 收斂 M1G**（pile-on 崩、失去 tier 身分）：軟化 P1 抹平「沉沒供給稀缺」。緩解＝$\gamma$ 撐；**可能不足——這是成敗未知數，靠 §6 驗證判定**。
- **R3 反釣魚封印移除**：原 P1/SG 有擋「z=0 釣 Pa partial」的作用。改由 $\gamma$ 成本壓「釣 partial」；須確認不出退化。
- **R4 求解變慢/變大**：放寬約束 + Pa 可服 partial → 變數/組合變多。監看 solveSec。

## 6. 驗證計畫（決定成敗）

1. **Build**：x64 Release 無誤。
2. **Zero-drift**：三 flag 全關，Fixed `orders1150` 跑，kpi/trips/decision-log 與現任 tier 逐位一致（三段 gated 區塊整段跳過 + decode 斷言保留）。
3. **死鎖治癒**：`setlevel` 臂在 Fill o200/o300 跑滿 14400s（對照 tier 凍@5624/1981）。
4. **效率（主判定）**：Fixed `orders1150` 掃 $\varepsilon_{prog} \in \{-0.5,-2,-5\}$ × $\gamma \in \{3,6,12\}$，比 tier（1048/1.888/3.302）與 M1G（959/1.228/4.605）。**成功條件（三者同時）＝(a) 死鎖治癒、(b) pile-on 不崩（≥ tier 雜訊帶）、(c) 完勝 M1G**。
5. **反碎裂交叉檢查**：掃描中監看 pile-on、splits、母單完成數，確認無 θ=2 式崩盤。
6. **找不到三贏點 → 回退 watchdog 路線、保現任 tier**（本重設計是探索，非承諾）。

## 7. 可回退性

- 全程 config-gated，三 flag 預設關 = 逐位等同現任 tier。
- 徹底移除 = revert config 欄位 + 三處 gated 區塊（目標式兩項、約束三條、decode 斷言）。
- 建議另開分支 `setlevel-redesign` 做，與現任 tier 隔離；現任 committed tier（`e3d842f`）不動。
