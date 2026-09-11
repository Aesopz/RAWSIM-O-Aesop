# M2e-IC 現行模型（close + pod-tier draw cost）派工數學模型 — as implemented

對象：`SplitM2eICManager` + `split_milp_m2eic_tier.xconf`（2026-07-21 現任主臂：close 結構 + pod 分層代價，力道 1/3）。
本文承接 `docs/2026-07-19-m2e-ic-close-dispatch-model.md`（close 基礎結構），差異只在 §4 目標式新增一項、§1.1 pod 狀態追蹤多一個集合。其餘約束式、決策變數、decode 流程完全繼承，逐條抄錄不省略，方便單獨閱讀。

一句話：每個決策時點解一個 MILP，聯合決定「派哪些新貨架去哪站（PS/TA）＋哪些訂單以整單/拆單形式吃進槽位（OA）」；OA 選擇要吃哪個貨架的內容時，依「正在被撿 > 排隊中 > 還在路上」三層由高到低給予遞增代價，軟性引導、不禁止任何選項。

---

## 0. 這一版跟 close 基礎版的唯一差異

close 版的槽位分配（§5 elink1-3/eshi4 等）只看「貨架庫存夠不夠」，完全不管貨架物理上在哪裡——正在被撿的貨架、剛到站排隊的貨架、還在路上的貨架，被同等對待。這在 2 站小規模不太出事（貨架供給週轉快），但規模一大（4 站/10 bots，bots/station 比從 5 降到 2.5）就會出現「訂單押注在還沒到站的貨架上、卻讓已經到站的貨架空等」的浪費。

修法不是硬性規則（試過三次都撞牆：scout 排擠、value-dispatch 拆單崩潰、slot-honesty/defer 撞上共用引擎的 `_assignedOrders`/Ziops 雙閘門，見 §8），是在目標式加一筆**軟性代價**：貨架離「正在被撿」這個狀態越遠，訂單想吃它的內容就要多付代價。模型永遠可以選擇「多付代價也要吃」，所以不會製造無解。

---

## 1. 決策時點（epoch 觸發）— 與 close 完全相同

MILP 不定時跑，由事件喚醒：

| 觸發 | 位置 |
|---|---|
| 任一訂單/child 在站完成（槽位釋放） | `OutputStation.cs:373` |
| 新訂單進 backlog | NewOrder 事件 |
| D16：當前 bot 任務只剩 1 個 pick（pod 臨終信號） | `OutputStation.cs:258-260, 302-304` |

---

## 1.1 前處理：pod 狀態分層（本版新增追蹤）

除了 close 版原有的 processing 集合，本版額外追蹤排隊中貨架的**具體 ID 集合**（close 版只有數量，本版需要身分才能定價）：

- $P_p(s)$：站 $s$ **正在處理**的 pod（bot 的 CurrentWaypoint = 站的揀貨 waypoint）
- $P_q(s)$：站 $s$ **排隊中**的 pod（bot 的 CurrentWaypoint 是 queue waypoint）——**本版新增明確 ID 追蹤**（`icQueuedByStation`）
- 「還在路上」= committed（$P_b$）但既非 $P_p(s)$ 也非 $P_q(s)$ 的貨架（含本輪剛從 $P_a$ 派出去的）——不需要額外集合，用排除法判定

---

## 2. 集合與參數（新增兩個）

沿用 `docs/2026-07-19-m2e-ic-close-dispatch-model.md` §2 全部符號，新增：

| 符號 | 意義 | 現行值 |
|---|---|---|
| $\alpha$ | 排隊中貨架每單位代價（QueuedPodDrawPenalty） | **1** |
| $\beta$ | 還在路上貨架每單位代價（OnTheWayPodDrawPenalty，$\beta \ge \alpha$） | **3** |

其餘符號（$w_1$=1、$w_2$=−40、$w_3$=5、$w_4$=10、$w_p$=12、$w_{pipe}$=20、$L$=70s、$T$=1、$w_{2p}$=20、$\varepsilon$=−0.5、$\varepsilon_{cov}$=−0.2）與 close 版完全相同，見前份文件 §2。

---

## 3. 決策變數 — 與 close 完全相同

$x_{p,s}$（pod→站）、$y^R_{b,p}$（bot→pod）、$q_{o,i,p,s}$（訂單/SKU/pod/站 unit 級取貨）、$y_{o,s}$（訂單佔槽）、$z_o$（本輪完整指派）、$w_o$（整單路徑）、$u_s$（空槽數）、$e_o$（多部位超額）、$\sigma_s$（pipeline 短缺）、$c_i$（覆蓋量）。定義見前份文件 §3。

---

## 4. 目標式（Minimize）— 新增一項

$$
\begin{aligned}
\min\ \ & w_1 \Big[ \sum_{p \in P_a}\sum_{s} \big(d^{PS}_{p,s} + w_4\big)\, x_{p,s} + \sum_{b \in B}\sum_{p \in P_a} d^{BP}_{b,p}\, y^{R}_{b,p} \Big] && \text{(a) 距離＋每趟固定成本} \\
+\ & w_2 \sum_{o \in O} z_o && \text{(b) 完成獎勵（−40）} \\
+\ & w_3 \sum_{s \in S} u_s && \text{(c) 空槽壓力（5）} \\
+\ & w_p \sum_{o \in O} e_o && \text{(d) 多部位懲罰（12）} \\
+\ & w_{pipe} \sum_{s \in S} \sigma_s && \text{(e) pipeline 短缺（20）} \\
-\ & w_{2p} \sum_{o \in O_{parent}} z_o && \text{(f) parent 收單加碼（20）} \\
+\ & \varepsilon \sum_{o,i,p,s} \text{scarcity}_i \cdot q_{o,i,p,s} && \text{(g) 稀缺榨取（−0.5）} \\
+\ & \varepsilon_{cov} \sum_{i} c_i && \text{(h) 覆蓋 tie-break（−0.2）} \\
+\ & \sum_{o,i,s}\ \sum_{p \,\notin\, P_p(s)}\ \pi(p,s) \cdot q_{o,i,p,s} && \text{(i) 【新增】pod 分層取貨代價}
\end{aligned}
$$

其中分層代價係數：

$$
\pi(p,s) = \begin{cases} 0 & p \in P_p(s)\ \text{（正在被撿，免費）} \\ \alpha & p \in P_q(s)\ \text{（排隊中）} \\ \beta & \text{其他（還在路上/本輪剛派出）} \end{cases}
$$

**性質**：這是連續、軟性的偏好，不是禁令——只要完成訂單的獎勵夠大（$|w_2|=40$ 遠大於 $\alpha,\beta$ 個位數量級），模型仍可以選擇吃排隊中甚至路上貨架的內容。目標式的其他七項（(a)-(h)）與 close 版逐項相同，不受影響；(i) 只在 $q>0$ 時才計費，$q=0$（不選這個 pod）永遠是可行解，故**此項不可能造成無解**（對照 §8 中三次硬規矩嘗試皆因排除了「不選」這個可行解而撞牆）。

程式碼位置：`SplitM2eICManager.cs`，目標式組裝區塊，`icQueuedPen`/`icOnTheWayPen` 變數，緊接在 w5（處理中貨架榨取獎勵）之後、eps（稀缺榨取）之前。

---

## 5. 約束式 — 與 close 完全相同，不重複抄錄

elink1-3（拆單機械）、eM1/eM2/eZfin（完成語意）、eshi4/6/7/8/9/10/13'（槽位與派車資源）、P1 全組（icP1a-d，含 close 例外）、SG 全組（icSG1-2）、D9（icD9）、D11（icLGcap/icLG1）、D14（iccov）、PK（關閉）——逐條定義見 `docs/2026-07-19-m2e-ic-close-dispatch-model.md` §5.1-5.9，本版一字不改。

---

## 6. Decode：解 → 派工動作 — 與 close 完全相同

見前份文件 §6。新增的分層代價只影響**求解結果**（模型會傾向選哪個 pod 的哪些單位），不影響 decode 邏輯本身——decode 拿到的還是同一組 $x/y/q/z$ 解，處理方式不變。

---

## 7. 實證結果（2026-07-21，5-seed 驗證）

### 7.1 small（2 站/10 bots）—— 機制形同虛設

站台飽和、貨架週轉快，「押注還沒到站貨架」的情境很少發生，分層代價幾乎沒有機會生效：

| | close | tier(1,3) |
|---|---|---|
| TP（5-seed 平均） | 611.6 | 609.0（差 −2.6，雜訊帶內） |
| 接縫次數 | 15~16 | 16~27（力道加重不減反增） |

單 seed 掃描 5 組力道（0.5/1.5 至 3/8）皆未突破雜訊帶，確認 **small 規模不是這個機制該驗證的地方**。

### 7.2 small_4o10b（4 站/10 bots，bots/station 比 5→2.5）—— 機制真正生效

| | close（5-seed 平均） | tier(1,3)（5-seed 平均） | 差 |
|---|---|---|---|
| TP | 1096.0 | 1094.4 | −1.6（雜訊帶內，**打平**） |
| Items | 2663.8 | 2658.8 | 打平 |
| **EOR（KJ/order）** | **1.2754** | **1.2436** | **−2.49%（5 個 seed 全部同向，零反例）** |

**結論**：這個機制**不是 TP 槓桿**（吞吐量統計上打平），是**同吞吐量下穩定換取能耗改善**的機制——5-seed 全部同向、EOR 降幅 0.8%~4.8%，是這整輪嘗試裡少數通過 5-seed 驗證、方向一致的正結果。

---

## 8. 已棄用/回退的路線（供後續不要重複踩雷）

1. **偵察派遣（scout, `AnticipatoryDispatchWeight`）**：讓提早派車跟訂單驅動派車共用同一份 icLGcap 名額——任何非零權重都造成排擠，劑量單調趨近 0（TP 收斂回 624）證明是排擠不是校準問題。程式碼保留、預設關閉。
2. **Value-Dispatch 單獨使用（`NewPodDispatchByValue`）**：拿掉派車必須被消費的硬性要求，改用拉高的覆蓋率權重當理由——TP 衝到 638（貼近 M1G 天花板）但 pile-on 從 8.40 崩到 4.64，拆單母單 116→4。根因：SG 拆單閘門需要「沒有車在排隊」，大方派車讓排隊變常態，拆單觸發窗口被填平。程式碼保留、預設關閉，可作為「TP 極端」對照臂。
3. **Slot-Honesty（`RequireProcessingPodForDraw`）**：不准任何訂單（整單/拆單）綁定未真正處理中的貨架——挖到三個共用引擎老 bug（貨架無 Ziops 配對即被放棄、補派篩選寫死需要非空清單、空清單呼叫 `.First()` 崩潰）皆已定位修復，但修復後撞上真正的 MILP 無解（Gurobi infeasible），已完整回退。
4. **Defer-Allocation（`DeferAllocationUntilProcessing`）**：只延後「正式排站」動作、配對登記照舊——manager-only、不碰引擎，理論上安全，但撞上 `OutputStation.RequestItemTake` 要求 `_assignedOrders.Contains(order)` 才放行（否則直接 Abort，不是排隊等）——確認「配對可早、排站可晚」在現行引擎下是不可分割的原子操作，非 manager 層能解，需要動到 `OrderManager.AllocateOrder`/`OutputStation.RegisterOrder` 這條鏈才可能真正做到，屬於更大規模的引擎工程。

**共同教訓**：任何想讓「派車/內容綁定」提早於「物理到站」的機制，只要用**硬規則**（禁止某個選項）都會在這個已運行十年的共用引擎上撞到未曾被測試過的耦合點；只有**軟性代價**（永遠保留「不選」這條退路）才能安全落地。

## 9. M1G 對照事實（決定性，供論文比較章節引用）

查證 M1G 原始碼確認：M1G 真正丟給 Gurobi 求解的變數只有 xps/yrp/yos/yaos/dops/us 六組，**幾件貨具體給哪張訂單（IC 的 q）從未進入求解器**——`deVarNameziops` 整段被註解掉，改用求解後的貪婪迴圈事後填入（優先用「本輪新分配的貨架」）。M1G 標榜的「聯合最佳化」只做到粗配對層級；IC 從一開始就把 unit 級分配也納入同一次聯合求解，比 M1G 更徹底、但也因此暴露了 M1G 從未面對的問題（M1G 的貪婪填入層天生只看「當下已知狀態」，間接迴避了 pod 狀態分層的必要性）。
