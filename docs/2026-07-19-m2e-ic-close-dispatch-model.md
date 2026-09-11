# M2e-IC（close 臂）派工決策數學模型 — as implemented

對象：`SplitM2eICManager` + `split_milp_m2eic_close.xconf`（現任主臂）。
本文件依程式碼逐條抄錄（檔案行號以 2026-07-19 的 branch `6/24` 為準），不是設計稿——寫的是**實際在跑的模型**。

一句話：每個決策時點解一個 MILP，聯合決定「派哪些新貨架去哪站（PS/TA）＋哪些訂單以整單/拆單形式吃進槽位（OA）」；解出後 decode 成引擎動作。

---

## 0. 決策時點（epoch 觸發）

MILP 不定時跑，由事件喚醒（`SituationInvestigated=false`）：

| 觸發 | 位置 |
|---|---|
| 任一訂單/child 在站完成（槽位釋放） | `OutputStation.cs:373` |
| 新訂單進 backlog | NewOrder 事件 |
| D16：當前 bot 任務只剩 1 個 pick（pod 臨終信號） | `OutputStation.cs:258-260, 302-304` |

實測（small, seed0, 7200s）：1156 次決策 ≈ 每 6.2 秒一次，平均求解 0.012s。

---

## 1. 前處理（每次求解前計算的常數）

### 1.1 Pod 四狀態分割

- $P_p(s)$：站 $s$ **正在處理**的 pod（bot 任務執行中）
- $P_q(s)$：已抵達站 $s$ **排隊中**的 pod
- $P_b$：**在途**（已派遣、行走中）的 pod——連同 $P_q$ 的指派用 `eshi7/eshi11` 固定，不可反悔
- $P_a$：**儲區未使用** pod——唯一的自由派遣對象

「committed 供給」= $P_p \cup P_q \cup P_b$。

### 1.2 Od 緊急過濾（承襲 M1G）

$O_d$ = deadline 緊迫訂單子集；當 $|O_d| > \sum_s C_s$（`odFired`）時 **pendingOrders ← $O_d$**（close 臂無 parent 例外）。實測 33% 的 epoch 觸發，觸發時模型視野平均縮到 2.1 張單。

### 1.3 稀缺度與閘門

- 稀缺度：$\text{scarcity}_i = \min\!\left(1,\ \dfrac{\text{backlog 需求}_i}{\text{全場 pool 供給}_i}\right)$
- **SG 拆單閘門**（per station，strict 模式）：
  $$\text{sgOpen}(s) = \big[P_p(s) \neq \emptyset\big] \wedge \big[|P_q(s)| = 0\big]$$
  拆單窗口 = successor 仍在行走、尚未到站的時段。（`M2eICMath.SplitGateOpen`）
- **D11 派遣閘門**（per station）：
  $$\text{pipeGate}(s) = \big[P_p(s) \neq \emptyset\big] \wedge \big[\text{releaseLeft}(s) \le L\big], \quad L = 70\text{s}$$
  $\text{releaseLeft}(s)$ = 當前 pod 剩餘揀貨投影 $(k-1)\times 10 + 3$ 秒（`OutputStation.cs:944`）；無 pod 處理中（NaN）→ 閘門關。
- pipeline 深度：$F(s) = |P_q(s) \cup P_b(s)\text{ 中派往 } s \text{ 者}|$（只數個數，**不看 ETA**）。

---

## 2. 集合與參數

| 符號 | 意義 | close 臂值 |
|---|---|---|
| $O$ | 本輪 pendingOrders（Od 過濾後） | — |
| $I$ | SKU 集合；$r_{o,i}$ = 訂單 $o$ 對 $i$ 的**剩餘**需求 | — |
| $S$ | 有容量的 output station；$C_s$ = 槽位容量 | $C_s = 6$ |
| $P$ | 模型內 pod（$P_a \cup P_b \cup P_q \cup P_p$）；$a_{p,i}$ = pod $p$ 的 $i$ 可用量 | — |
| $B$ | 閒置 bot 集合 $R_a$ | — |
| $d^{PS}_{p,s},\ d^{BP}_{b,p}$ | pod→站 / bot→pod 距離成本 | — |
| $w_1$ | 距離權重 | 1（寫死） |
| $w_2$ | 完成獎勵（OrderRewardWeight） | **−40** |
| $w_3$ | 空槽懲罰（IdleSlotWeight） | **5** |
| $w_4$ | 新行程固定成本（PodTripFixedCost） | **10** |
| $w_p$ | 多部位懲罰（MultiPartPenalty, D9） | **12** |
| $w_{pipe}$ | pipeline 短缺懲罰（PipelineFloorWeight, D11） | **20** |
| $L,\ T$ | 派遣 lead / pipeline 目標深度 | **70s / 1** |
| $w_{2p}$ | parent 收單加碼（ParentClosingReward） | **20** |
| $\varepsilon$ | 稀缺加權榨取獎勵（UnitDrawReward） | **−0.5** |
| $\varepsilon_{cov}$ | 選中集覆蓋獎勵（CoverageRewardWeight, D14） | **−0.2** |
| PK | packing 容量（PackingStationCount × PackingBufferCapacity） | **關**（=0） |
| $w_5$、PR | processing 榨取獎勵 / pro-rata | 0 / 關 |

ParentClosingDispatch = **true**（close 臂的定義性開關，見 icP1d 與 decode）。

---

## 3. 決策變數

| 變數 | 域 | 意義 |
|---|---|---|
| $x_{p,s}$ | $\{0,1\}$ | pod $p$ 指派到站 $s$（$P_b/P_q/P_p$ 固定 =1；**$P_a$ 的 $x=1$ 即「派工」**） |
| $y^{R}_{b,p}$ | $\{0,1\}$ | bot $b$ 承接 pod $p$ 的行程（TA） |
| $q_{o,i,p,s}$ | $\mathbb{Z}_{\ge 0}$ | 訂單 $o$ 的 SKU $i$ 從 pod $p$ 在站 $s$ 取貨量（**unit 級拆單變數**） |
| $y_{o,s}$ | $\{0,1\}$ | 訂單 $o$ 在站 $s$ 開一個部位（slot/child 指標） |
| $z_o$ | $\{0,1\}$ | `zdonex`：$o$ 的剩餘需求本輪**完整指派**（可跨站） |
| $w_o$ | $\{0,1\}$ | `icwholex`：$o$ 走整單路徑（單站、可用 $P_a$） |
| $u_s$ | $\mathbb{Z}_{\ge 0}$ | 站 $s$ 空槽數 |
| $e_o$ | $\mathbb{Z}_{\ge 0}$ | `icepx`：$o$ 的多部位超額（部位數 −1） |
| $\sigma_s$ | $\mathbb{Z}_{\ge 0}$ | `icsfx`：站 $s$ 的 pipeline 短缺 |
| $c_i$ | $\mathbb{R}_{\ge 0}$ | `iccov`：選中 pod 集對池需求 $i$ 的覆蓋量 |

---

## 4. 目標式（Minimize）

$$
\begin{aligned}
\min\ \ & w_1 \Big[ \sum_{p \in P_a}\sum_{s} \big(d^{PS}_{p,s} + w_4\big)\, x_{p,s} \;+\; \sum_{b \in B}\sum_{p \in P_a} d^{BP}_{b,p}\, y^{R}_{b,p} \Big] && \text{(a) 距離＋每趟固定成本} \\
+\ & w_2 \sum_{o \in O} z_o && \text{(b) 完成獎勵（} w_2 = -40 \text{）} \\
+\ & w_3 \sum_{s \in S} u_s && \text{(c) 空槽壓力（=5，已降格）} \\
+\ & w_p \sum_{o \in O} e_o && \text{(d) 多部位懲罰（=12）} \\
+\ & w_{pipe} \sum_{s \in S} \sigma_s && \text{(e) pipeline 短缺（=20）} \\
-\ & w_{2p} \sum_{o \in O_{parent}} z_o && \text{(f) parent 收單加碼（=20）} \\
+\ & \varepsilon \sum_{o,i,p,s} \text{scarcity}_i \cdot q_{o,i,p,s} && \text{(g) 稀缺榨取（} \varepsilon = -0.5 \text{）} \\
+\ & \varepsilon_{cov} \sum_{i} c_i && \text{(h) 覆蓋 tie-break（} \varepsilon_{cov} = -0.2 \text{）}
\end{aligned}
$$

權重塔（絕對值）：$|w_2| = 40 > w_{pipe} = 20 > w_p = 12 > w_4 = 10 > w_3 = 5 \gg |\varepsilon|, |\varepsilon_{cov}|$。
（程式碼：目標組裝 `SplitM2eICManager.cs:627-745`）

---

## 5. 約束式

### 5.1 供給與指派鏈結（拆單機械）

$$\sum_{o} q_{o,i,p,s} \le a_{p,i}\, x_{p,s} \qquad \forall i, p, s \tag{elink1}$$
$$y_{o,s} \le \sum_{i,p} q_{o,i,p,s} \qquad \forall o, s \tag{elink2}$$
$$\sum_{p} q_{o,i,p,s} \le r_{o,i}\, y_{o,s} \qquad \forall o, i, s \tag{elink3}$$

elink1：跨訂單合計不得超過 pod 實際庫存且需 pod 被指派；elink2：禁止空 child；elink3：有取貨必開部位（槽位會計誠實）。

### 5.2 完成語意（cross-time，zdonex）

$$\sum_{p,s} q_{o,i,p,s} \le r_{o,i} \qquad \forall o,\ i \in \text{可見SKU} \tag{eM2}$$
$$\sum_{p,s} q_{o,i,p,s} \ge r_{o,i}\, z_o \qquad \forall o,\ i \in \text{可見SKU} \tag{eM2done}$$

$z_o = 1$ ⟺ 所有**在架可見** SKU 的剩餘需求本輪足額指派（legacy zdonex：缺貨 SKU 跳過——由 icP1oos 補洞）。

### 5.3 槽位與派車資源

$$\sum_{o} y_{o,s} = C_s - u_s \qquad \forall s \tag{eshi4}$$
$$\sum_{s} x_{p,s} \le 1 \qquad \forall p \tag{eshi6}$$
$$x_{p,s} = 1,\ \ y^{R}_{b(p),p} = 1 \qquad \forall p \in P_b \cup P_q\ (\text{承襲固定}) \tag{eshi7/11}$$
$$\sum_{s} x_{p,s} \le \sum_{b} y^{R}_{b,p},\qquad \sum_{b} y^{R}_{b,p} \le 1,\qquad \sum_{p} y^{R}_{b,p} \le 1 \tag{eshi8/9/10}$$
$$x_{p,s} \le \sum_{o,i} q_{o,i,p,s} \qquad \forall p \in P_a, s \tag{eshi13'}$$

eshi13'：新派的 pod 必須真的被消費（不派空車）。

### 5.4 P1：拆單只吃 committed 供給（本模型核心紀律）

對每張單 $o$，令 $Q^{Pa}_o = \sum_{i,\,p \in P_a,\,s} q_{o,i,p,s}$（從儲區 pod 的總取量）：

$$w_o \le z_o \tag{icP1a}$$
$$\sum_{s} y_{o,s} + (|S|-1)\, w_o \le |S| \tag{icP1b}$$
$$w_o = 0 \quad \forall o \in \{\text{split parent}\} \cup \{\text{有缺貨殘需者}\} \tag{icP1c/icP1oos}$$
$$Q^{Pa}_o \le M_o \cdot \begin{cases} z_o & o \text{ 為 parent 且 ParentClosingDispatch}=\text{true} \\ w_o & \text{否則} \end{cases} \tag{icP1d}$$

白話：動用儲區供給的訂單必須是「本輪完成＋單站」的整單（$w_o$）；**close 臂例外**——split parent 可為「一次關滿」（$z_o = 1$，全部殘需本輪指派完）動用儲區 pod ＝ 專車收尾。partial 釣魚（$z=0$ 抽 $P_a$）在兩種情況下都不可能。$M_o = \sum_i r_{o,i}$。

### 5.5 SG：拆單時機閘門（strict）

對 $\text{sgOpen}(s) = \text{false}$ 的站、非 parent 的訂單：

$$y_{o,s} \le z_o \tag{icSG1}$$

（閘門關閉時，新鮮單要佔槽就必須本輪完成——新 partial 只准出現在「Pp 垂死、successor 未到」的橋接窗。）

新鮮單的 partial 取貨來源再收一層——只准抽**處理中**的 pod：

$$\sum_{i,s}\sum_{p \in P_b \setminus P_p(s)} q_{o,i,p,s} \le M_o\, z_o \qquad \forall o \notin \text{parents} \tag{icSG2}$$

### 5.6 D9：多部位計數

$$\sum_{s} y_{o,s} \le e_o + 1 \qquad \forall o \tag{icD9}$$

### 5.7 D11：pipeline floor（**派工的時鐘驅動路徑**）

對每站 $s$，令 $X^{new}_s = \sum_{p \in P_a} x_{p,s}$，$F(s)$ = 在途數：

$$X^{new}_s \le \max(0,\ T - F(s)) \qquad \forall s \tag{icLGcap 硬上限}$$
$$X^{new}_s + \sigma_s \ge T - F(s) \qquad \forall s:\ \text{pipeGate}(s) = \text{true} \tag{icLG1 軟下限}$$

閘門開（releaseLeft ≤ 70s）而不派 successor，就吃 $w_{pipe}\sigma_s = 20$ 的罰——把「訂單驅動派車不足」的缺口補上。

### 5.8 D14：覆蓋 tie-break

$$c_i \le \text{池需求}_i, \qquad c_i \le \sum_{p:\, a_{p,i}>0}\sum_s a_{p,i}\, x_{p,s} \tag{iccovd/iccovs}$$

### 5.9 PK：packing 箱位（close 臂關閉）

容量 $C_{pack} = \text{PackingStationCount} \times \text{PackingBufferCapacity}$（一 parent 一箱、consolidation 釋放）。close 臂 xconf 明寫 0 ⇒ 本塊不生成。

---

## 6. Decode：解 → 派工動作

1. **新行程（PS/TA 派工）**：$x_{p,s} = 1,\ p \in P_a$ ＋ $y^R_{b,p} = 1$ → 生成 ExtractTask，bot $b$ 去取 pod $p$ 送站 $s$。
2. **整單路徑（fastPath）**：單一部位＋覆蓋全部殘需＋非 parent → 直接 `AllocateOrder(o, s)`，不拆。
3. **拆單路徑**：其餘每個部位 `Order.CreateSplitChild` → child 佔槽；decode 斷言 $Q^{Pa}_o = 0$（close 例外：parent 且本輪關滿）；parent 首拆時 `PackingBuffer.RegisterParent`。
4. **後續注入**：pod 在站期間，之後 epoch 的 q 解繼續對同 pod 加 pick（on-the-fly 窗口 `Requests.Any()`）——實測 51% 的揀貨經此路徑。
5. **Consolidation**：最後一個 child 完成 → parent 完成事件 + `ReleaseParent`（`OutputStation.cs:354-368`）。

---

## 7. 已知結構弱點（2026-07-19 實測快照）

1. **換架接縫**：派工被 §5.7 的 70s lead 閘門時序化——行走時間 > releaseLeft 即漏縫。15 次/260s（PVGS 5 次/56s），≈ 全部 TP 差距（624 vs 658）的物理來源。SG-strict（§5.5）是接縫的結構原因：successor 早到會關閉拆單窗，所以派遣不敢早。
2. **$F(s)$ 不看 ETA**：在途 pod 無論多遠都滿足 $T=1$，icLGcap 反而擋住補派。
3. **Od 過濾**（§1.2）：33% epoch 視野縮到 ~2 張單，該時刻的派車對池失明。
4. **pool 估值失聲**：pod 選擇由 12 槽內的 $z$ 主導，池級價值只剩 $\varepsilon/\varepsilon_{cov}$ 耳語（對照 M1G 的 yos/shi5 池級估值——但彼承諾不誠實）。
