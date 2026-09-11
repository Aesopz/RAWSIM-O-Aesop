# M2e 模型完整定義（SplitM1GExact，CrossTime = true）

依據當前實作逐條對齊：`RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs`（權重讀取 :345-349、目標式 :372-392、限制式 :393-481）。M1e（CrossTime = false）僅差最後一組限制式，見 §7。

---

## 0. 一句話定位

事件觸發的快照式（rolling-horizon）MILP：每次決策對「當下系統狀態」一次性聯合決定 **pod 出動（PS）、機器人指派（TA）、以及每張訂單的每個 SKU 從哪個 pod 在哪個站揀幾個（OA，unit 級、可部分指派）**。殘量留在 backlog 由未來的決策接手——跨期拆單由此實現，模型本身沒有時間軸。

---

## 1. 集合與索引

| 符號 | 定義 | 程式碼來源 |
|---|---|---|
| $\mathcal{S}$ | 仍有空槽的揀貨站 | `Cs.Keys` |
| $\mathcal{O}$ | 本期入模訂單：backlog 中**至少一個殘量 SKU 有可見庫存**者（M2e 准入；M1e 要求全部殘量可滿足）。若急單池 $O_d$（Timestay 逾 30 min）數量超過總空槽，則縮池 $\mathcal{O} := O_d$ | :286-292 |
| $\mathcal{I}$ | SKU 全集；$\mathcal{I}_o \subseteq \mathcal{I}$ = 訂單 $o$ 仍有殘量且模型可見的 SKU | `residuals[o]` |
| $\mathcal{P}_b$ | 沿襲 pod：已被認領、在站或在途（processing + queueing + on-the-way，不區分） | :230-256 |
| $\mathcal{P}_a$ | 可動用 pod：在儲位上、無 bot 綁定、含背單所需 SKU | :257-266 |
| $\mathcal{P} = \mathcal{P}_a \cup \mathcal{P}_b$；$\mathcal{P}_i \subseteq \mathcal{P}$ | 含 SKU $i$ 庫存的 pod | `PiSKU` |
| $\mathcal{P}^{\mathrm{proc}} \subseteq \mathcal{P}_b$ | **processing pods**：機器人已停在某站揀貨點（`bot.CurrentWaypoint == station.Waypoint`）的 pod。僅 $w_5 \neq 0$ 時建構 | :350-359 |
| $\mathcal{R}_a$ | 空閒機器人（無 pod、非佔用；含可徵用的 return-pending） | :267-284 |
| $\sigma(p),\ \rho(p)$ | $p \in \mathcal{P}_b$ 的既定站台與既定機器人（上期決策的結果） | `inboundPods` / `PodToBot` |

## 2. 參數

| 符號 | 定義 | 值 / 來源 |
|---|---|---|
| $r_{o,i}$ | 訂單 $o$ 對 SKU $i$ 的**殘餘需求**（總需求 − 已被 child 認領；需求帳本 `RemainingPositions`） | 快照 |
| $k_{p,i}$ | pod $p$ 上 SKU $i$ 的可用庫存 | `pod.CountAvailable(i)` |
| $C_s$ | 站 $s$ 的空槽數（容量 − 已掛訂單數） | 快照 |
| $d_{p,s}$ | pod→站距離代理 + 附加成本（`ExactPodStationCost + PodStationExtraCost`，自由流距離，不含壅塞） | 幾何 |
| $d^{RP}_{r,p}$ | 機器人→pod 距離代理（`ExactBotPodCost`） | 幾何 |
| $w_1$ | 距離成本權重 | $1$（寫死） |
| $w_2$ | 訂單完成獎勵（`OrderRewardWeight`） | $-40$（沿用 online.pdf） |
| $w_3$ | 空槽罰款（`IdleSlotWeight`） | 原版 $1000$；對齊掃描定版 $0$ |
| $w_4$ | **每趟新 pod 行程固定成本**（`PodTripFixedCost`） | default $0$；掃描定版候選 $40$ |
| $w_5$ | **processing pod 抽取獎勵**（`ProcessingPodDrawReward`，取負值為獎勵） | default $0$ |
| $K$ | 每期新 pod 行程上限（`MaxNewPodTripsPerDecision`，ecap） | default $0$ = 不設限 |
| $\bar{q},\ \bar{u}$ | 變數上界：$\bar{q}=\max_{o,i} r_{o,i}$、$\bar{u}=\max_s C_s$ | :360-364 |

## 3. 決策變數

| 變數 | 域 | 意義 |
|---|---|---|
| $x_{p,s}$ | $\{0,1\}$ | pod $p$ 指派到站 $s$（$p\in\mathcal{P}_b$ 被固定，見 eshi7） |
| $y_{r,p}$ | $\{0,1\}$ | 機器人 $r$ 去接 pod $p$ |
| $q_{i,o,p,s}$ | $\{0,\dots,\bar{q}\}$ | **核心 4D 變數**：訂單 $o$ 的 SKU $i$，從 pod $p$ 在站 $s$ 揀取的件數。僅對 $i\in\mathcal{I}_o,\ p\in\mathcal{P}_i,\ s\in\mathcal{S}$ 建變數 |
| $v_{o,s}$ | $\{0,1\}$ | 訂單 $o$ 在站 $s$ 開 child、佔一個槽（程式碼 `yspx`） |
| $z_o$ | $\{0,1\}$ | 訂單 $o$ 的模型可見殘量**本期全數指派完**（程式碼 `zdonex`；領 $w_2$ 獎勵的旗標） |
| $u_s$ | $\{0,\dots,\bar{u}\}$ | 站 $s$ 未使用的空槽數 |

## 4. 目標函數（最小化）

$$
\min\;
w_1\Bigg[\underbrace{\sum_{p\in\mathcal{P}_a}\sum_{s\in\mathcal{S}}\big(d_{p,s}+w_4\big)\,x_{p,s}}_{\text{新 pod 行程：距離＋行程稅}}
\;+\;
\underbrace{\sum_{r\in\mathcal{R}_a}\sum_{p\in\mathcal{P}_a} d^{RP}_{r,p}\,y_{r,p}}_{\text{空閒 bot 取 pod 距離}}\Bigg]
\;+\;
w_2\sum_{o\in\mathcal{O}} z_o
\;+\;
w_3\sum_{s\in\mathcal{S}} u_s
\;+\;
w_5\sum_{o\in\mathcal{O}}\sum_{i\in\mathcal{I}_o}\sum_{p\in\mathcal{P}^{\mathrm{proc}}\cap\,\mathcal{P}_i}\sum_{s\in\mathcal{S}} q_{i,o,p,s}
$$

白話：付「距離＋開趟稅」買「訂單揀完」（$w_2<0$ 是折抵）；$w_3$ 逼它把槽填滿；$w_5<0$ 引導把單撥給**正在揀貨**的 pod（排隊/在途 pod 刻意不給——它們的服務窗口還沒開始關）。

實作註記：
- 距離成本與 $w_4$ 只掛在 $\mathcal{P}_a$（新 pod）上；$\mathcal{P}_b$ 已在途，沉沒成本不再計。
- $w_4$ 折進 $x_{p,s}$ 的係數；因 eshi6 保證 $\sum_s x_{p,s}\le 1$，係數加法 = 精確的每趟固定成本。
- $w_5$ 項在 $\mathcal{P}^{\mathrm{proc}}$ 為空或其上無對味 $q$ 變數時整項省略（空序列 guard，:383-391）。

## 5. 限制式

**(ecap) 序列行程紀律**（$K>0$ 時才加，:396-402）
$$\sum_{p\in\mathcal{P}_a}\sum_{s\in\mathcal{S}} x_{p,s} \;\le\; K$$
每期最多開 $K$ 趟新 pod 行程。

**(elink1) pod 庫存上限＋供給側連動**（$\forall\, i,\ p\in\mathcal{P}_i,\ s$，:407-412）
$$\sum_{o\in\mathcal{O}} q_{i,o,p,s} \;\le\; k_{p,i}\; x_{p,s}$$
**跨訂單合計**不得超抽同一 pod 的同一 SKU；且只要有抽取，該 pod 必須真的派往該站。（逐訂單上界的版本會讓多張單各自榨滿同一 pod——煙霧測試崩潰後修正為聚合式。）

**(elink2) 禁止空 child**（$\forall\, o,\ s$，:414-415）
$$v_{o,s} \;\le\; \sum_{i\in\mathcal{I}_o}\sum_{p\in\mathcal{P}_i} q_{i,o,p,s}$$
不揀任何東西就不准佔槽。

**(elink3) 需求側連動＋殘量上限**（$\forall\, o,\ i\in\mathcal{I}_o,\ s$，:421-431）
$$\sum_{p\in\mathcal{P}_i} q_{i,o,p,s} \;\le\; r_{o,i}\; v_{o,s}$$
反向鎖：只要在站 $s$ 有揀訂單 $o$，就**必須**佔槽（$v_{o,s}=1$），且單一 SKU 在單站的抽取不得超過殘量。（漏掉此條時 solver 白拿獎勵不佔槽、站台超收 6 倍——第二個煙霧崩潰的修正。）

**(eshi4) 槽位守恆**（$\forall\, s$，:433-434）
$$\sum_{o\in\mathcal{O}} v_{o,s} \;=\; C_s - u_s$$
掛上的 child 數＋剩餘空槽＝容量，$u_s$ 由此被定義（並被 $w_3$ 懲罰）。

**(eshi6) pod 至多一站**（$\forall\, p$，:436-437）
$$\sum_{s\in\mathcal{S}} x_{p,s} \;\le\; 1$$

**(eshi7 / eshi11) 沿襲固定＝線上不可反悔**（$\forall\, p\in\mathcal{P}_b$，:439-448）
$$x_{p,\sigma(p)} = 1, \qquad y_{\rho(p),\,p} = 1$$
上期已認領的 pod 與其機器人本期不得改派——快照間的狀態連續性，即「線上性」的來源。

**(eshi8) 派 pod 必配 bot**（$\forall\, p$，:450-451）
$$\sum_{s\in\mathcal{S}} x_{p,s} \;\le\; \sum_{r\in\mathcal{R}} y_{r,p}$$

**(eshi9 / eshi10) 一對一配對**（$\forall\, p$；$\forall\, r$，:453-457）
$$\sum_{r\in\mathcal{R}} y_{r,p} \;\le\; 1, \qquad \sum_{p\in\mathcal{P}} y_{r,p} \;\le\; 1$$

**(eshi13') 新 pod 不空趟**（$\forall\, p\in\mathcal{P}_a,\ s$，:460-465）
$$x_{p,s} \;\le\; \sum_{o\in\mathcal{O}}\sum_{i\in\mathcal{I}_o:\,p\in\mathcal{P}_i} q_{i,o,p,s}$$
新認領的 pod 必須至少被抽一件——與 elink1 合成雙向鎖：抽貨 ⇔ 派 pod。（只對 $\mathcal{P}_a$：$\mathcal{P}_b$ 的 pod 被 eshi7 固定，可能已無對味庫存，不能強制消耗。）

**(eM2) 跨期部分指派——M2e 的靈魂**（$\forall\, o,\ i\in\mathcal{I}_o$，:477）
$$\sum_{p\in\mathcal{P}_i}\sum_{s\in\mathcal{S}} q_{i,o,p,s} \;\le\; r_{o,i}$$
本期可以只指派殘量的一部分（含 0）；未指派的殘量自動留在 backlog，由未來的決策快照接手——**跨期拆單**由此產生，且無需任何顯式時間變數。

**(eM2done) 全指派才領獎**（$\forall\, o,\ i\in\mathcal{I}_o$，:478）
$$\sum_{p\in\mathcal{P}_i}\sum_{s\in\mathcal{S}} q_{i,o,p,s} \;\ge\; r_{o,i}\; z_o$$
$z_o=1$（領 $w_2$ 獎勵）要求**每個**可見 SKU 的殘量都在本期指派完畢；半套沒有獎勵——獎勵是 per-order 而非 per-unit（修正 Spec 2 的 per-unit confound：大單不再天生比多張小單更值錢）。

## 6. 解的落地（模型之外、決策之內）

求解後（:482-487 起）：
1. $q$ 的正值直接餵 `SplitMilpDecoder.Decode` → 依站聚合建 **child orders**（Spec 1 的需求帳本原子扣帳）；
2. 每筆 $(i,\ \text{child},\ p,\ s)$ 依 $q$ 值寫死揀貨歸屬（`Ziops`）——**無**貪婪 pod-attribution、**無** dops 代理、**無**反悔機制（elink1 + elink3 + eshi13' 已使 pod 用量 by construction 精確）；
3. 新 pod 依 $y_{r,p}$ 派機器人認領。

## 7. M1e 變體（CrossTime = false）

僅兩處不同：
1. 訂單准入：要求**全部**殘量 SKU 皆可滿足才入模（:288-289）；
2. eM2 + eM2done 換成單條 **eM1**（:474）：
$$\sum_{p\in\mathcal{P}_i}\sum_{s\in\mathcal{S}} q_{i,o,p,s} \;=\; r_{o,i}\; z_o \qquad \forall\, o,\ i\in\mathcal{I}_o$$
本期 all-or-nothing：整張殘量一次指派完（可跨站拆），或整張不動。無時間彈性——這是 M1e 在 $w_4$ 行程稅下崩潰（535 vs M2e 631）而 M2e 優雅吸收的結構原因：「行程稅需要時間彈性才付得起」。

## 8. 模型看不見的東西（誠實邊界）

1. **時間**：$d_{p,s}$ 是自由流距離代理，不含 WHCA* 壅塞/排隊；「跨期」靠殘量留 backlog 實現，無顯式時間軸。
2. **佇列相位**：$\mathcal{P}_b$ 內排隊第 1 與排最後的 pod 等值；唯一的相位資訊是 $w_5$ 的二元「processing vs 其他」，且不含「多久後輪到」。
3. **未來**：不知道後續到單與 pod 釋放後的去向——epoch-myopic。每期最優 ≠ 軌跡最優（PVGS 反超事件的結構根源；ecap/w4/w5 皆無法完全復刻貪婪的序列節奏優勢，60+ runs 系統性排除）。
4. **回程**：pod 釋放後的放回不在模型內（研究定案：一律 Nearest 放回；回程能耗**觀測不建模**——榨乾策略天然產生輕貨架回程，由總能耗 KPI 量測）。

---

*2026-07-11。若與程式碼衝突，以 `SplitM1GExactManager.cs` 現狀為準。相關文件：`docs/2026-07-09-splitm1g-exact-model-explained.md`（M1e/M2e 與 M1G 的差異拆解）、`docs/superpowers/specs/2026-07-07-splitm1g-exact-design.md`（設計 spec 與兩次數學修正記錄）。*
