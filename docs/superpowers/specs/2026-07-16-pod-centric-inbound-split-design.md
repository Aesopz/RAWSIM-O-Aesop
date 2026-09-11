# Spec：M2e-IC — 貨架中心、入站限定的線上拆單 MILP（修正 M2e）

> 日期：2026-07-16（v1）→ 2026-07-17（**v4 定稿**）　分支：6/24
> 狀態：**設計定稿**（brainstorm 多輪收斂完畢，writing-plans 已同步）
> 事實來源：程式碼 / git log / memory。
> 命名為**暫定**（`M2e-IC` = Inbound-Committed Split）。保留 `M2e` 字首是為了論文 ablation 血緣。
> **版本軌跡**：v1 = P1+PK 結構；v3 = 五層權重階層（廢除 w3=1000 單一驅動，見 §5.0）+ D9 多站懲罰 + D11 pipeline floor；**v4 = 四 pod 狀態（Pp/Pq/Pb/Pa）+ SG 拆單閘門 + 覆蓋獎勵（cpool）+ 稀缺度加權 ε + pod-nearing-release 觸發**。

前置與血緣：
- 拆單合併語意骨架 = **Xie et al. (2021) split-over-time**（`paper/split.pdf` §3.4 + 附錄 B）。
- 線上聯合 + pod 狀態 = **Jiao et al. (2026) M1G**（EE-RAWSim-O 本體）。
- 修正對象 = **M2e**（`SplitM1GExactManager.cs`）。
- 行為藍本 = **PVGS 的四段禮拜儀式**（§5.3——已被實證正確的行為，本模型把它從貪婪迴圈升格為聯合最優化）。
- 已排除的死路（不要重提）：目標式端獎勵重塑（eps flat / CF 字典序 / pro-rata 三度陣亡）、字典序 sunk-first（五 seed FAIL）、天真晚綁定旗標（引擎 pre-claim，已死）、θ=2 partial 氾濫（518 崩潰）。
- **標準測試案例（2026-07-17 使用者確認）**：`Cs[s]=6`（`OStationCapacity=6`，`small.xlayo:21` 已核對）；`ItemTransferTime=10`（站台每件鎖 10s）、`ItemPickTime=3`（bot 每件等 3s）；pod 釋放 = `(剩餘件數−1)×10+3`（`OutputStation.cs:465`）。

---

## 1. 定位：修正 M2e 的哪一個病，以及怎麼修

**M2e 的病灶**（診斷鏈已見骨）：

1. **早綁定 + 綁到未到站的 pod**：`Pa`（儲區）被納入拆單候選 + `shi13'` 強迫消耗 → 訂單綁到還在儲區的 pod、佔槽空轉。
2. **拆單手足時間不同步**：child 綁在到站時間天差地別的 pod → 母單卡等最慢 child → consolidation 尾巴 3781s（vs PVGS 48s）。
3. **薄榨**：pile-on 4.6 vs 8.28；pod 到訪 306 vs 168——派太多薄 pod、不深榨現場 pod。
4. **（v4 補）M1G 家族的承諾誠實性缺口**：`yos/yaos` 雙層——`yos` 領完成獎勵、`yaos` 才有槽位＋Ziops 預留；`yos`-only 的「預計完成」**零預留、會蒸發**（§7.1）。

**修正的一句話**：完成訂單是主旋律，拆單是**橋接手段**——只在 processing pod 供給耗盡、下一顆未到時，用 partial 榨乾現場 pod；拆單的每一分服務都來自沉沒供給、絕不觸發新趟次；獎勵嚴格綁定「進槽＋逐件預留」，不付給未預留的承諾。

**模組邊界（硬性約束）**：`M1GManager.cs` / `HADGSManager.cs` / `SplitM1GExactManager.cs` / `PVGSManager.cs` **一行不動**。M2e-IC = 新鏡像檔 `SplitM2eICManager : M1GManager`。

---

## 2. 設計決策總表（多輪 brainstorm 定案；v3 起新增標 ★、v4 標 ◆）

| # | 決策 | 定案 |
|---|---|---|
| D1 | 拆單 child 的 pod 候選 | 只允許 committed pod；**v4 細化：新 partial 只准抽 `Pp`（processing）**，`Pq`/`Pb` 服務完成類與既有 parent 續完，`Pa` 只服務 whole |
| D2 | 整單（whole）的 pod 候選 | 可用 `Pa`（保留 fresh-dispatch，兼作供給管線 seed——引擎硬需求：trip 至少一個真實請求） |
| D3★ | 榨乾驅動力 | 不加 partial 獎勵；靠 P1 + 五層權重階層 + 成本不對稱湧現 |
| D4 | 完成獎勵 | per-order all-or-nothing `zdone`（`w2`） |
| D5 | 拆單型態 | 跨站 + 跨期皆允許（殘量留 backlog = Xie split-over-time） |
| D6 | 下游 consolidation | packing buffer `C=78`（Xie 附錄 B），config-gated，預設關 |
| D7 | consolidation 時間成本 | 忽略（Xie 明文），只計空間 |
| D8 | 供給補充 | `Pa` 經 whole-seed 派遣進管線；D11 floor 保節奏 |
| D9★ | 多站拆單懲罰 | `wp·ep[o]`：每多佔一站-part 罰一次（軟性；`wp∈[11,35]` 校準，起點 12） |
| D10★ | 填槽壓力角色 | `w3'≈5`（自 1000 降級）：只當橋接誘因，不當主驅動（§5.0 反向誘因教訓） |
| D11★ | 派工時機 | lead-gated pipeline floor：`releaseLeft ≤ 70s` 或無 processing pod 時，floor 罰款逼派工（AE 實測 target=2/lead=70 最佳；把事件規則改寫成 MILP 線性式） |
| D12◆ | 四 pod 狀態 | `Pp`（processing）/`Pq`（到站排隊）/`Pb`（在途）/`Pa`（儲區）；SG 閘門掛 `Pq`——**拆單窗口 = successor 的行走窗**（掛 `Pb` 會讓 floor 一派車就關閉拆單，節奏即死） |
| D13◆ | SG 拆單閘門 | 閘門關閉的站：新鮮單 `ysp ≤ zdone`（要嘛本期完成、要嘛別佔槽）；**只擋新開 partial，不擋完成類與既有 parent 續完**；兩模式 config：strict（`Pp∧Pq=∅`）/ loose（`Pp` 存在即可）——ablation 裁決 |
| D14◆ | pod 集合價值 | 完成數主導（`w2`）+ **殘餘覆蓋 tie-break**（cpool 連續變數，CF 機械復用但**小權重加法項、非字典序**）；同完成數的 pod 集合，總覆蓋差 = 殘餘覆蓋差，免顯式扣除 |
| D15◆ | 榨取深度 tie-break | `ε` 從 flat 改 **pool-local 稀缺度加權**：`ε·Σ q·scarcity[i]`，`scarcity[i]=min(1, backlog需求/本輪候選pod池供給)`（與 PVGS sigma 同式、分母改本輪池） |
| D16◆ | 決策觸發 | 既有 order-completion 觸發之外，新增 **pod-nearing-release 觸發**：processing pod 的 ExtractTask 剩 1 件時 poke `SignalOrderFinished`（其 body 僅設 `SituationInvestigated=false`，`OrderManager.cs:204-207`）→ 重解在 on-the-fly 窗口（`Requests.Any()`）關閉**前**跑，收割 case-A 續榨機會 |

---

## 3. 四 Pod 狀態（v4 核心資料軸）

| 狀態 | 引擎判準 | 目標式成本 | 可服務範圍 |
|---|---|---|---|
| `Pp` processing | `PodToBot[p].CurrentWaypoint.ID == station.Waypoint.ID` | 0（沉沒） | 一切，**含新 partial（SG 閘門開時）** |
| `Pq` queueing | bot 在站台 queue waypoint（`IsQueueWaypoint`） | 0 | 完成類（whole/多站完成）+ 既有 parent 續完 |
| `Pb` inbounded（在途） | claimed、已註冊 inbound、未到站 | 0 | 同 `Pq` |
| `Pa` unused | `UnusedPods` 在儲位 | `w4 + w1·d` | **僅 whole**（P1） |

- 「inbounded」的精確定義是**有 claim + 有待撿請求**，不是「人在現場」：最後一件撿完的瞬間，pod 掉出 `P^C`、且回儲途中兩邊都不是（模型盲區）——D16 觸發器的存在理由。
- 狀態機時序：`Pa` --(whole-seed 派遣)--> `Pb` --(到站)--> `Pq` --(輪到)--> `Pp` --(佇列清空)--> 釋放回儲。
- 冷啟動性質：`t=0` 時 committed 集合為空 → P1 逼全部走 whole → **第一期行為 = M1G**；拆單嚴格是「利用沉沒供給」的行為。

---

## 4. 數學模型

### 4.1 集合與參數

- `O`：pending 訂單（fresh + 跨期 parent）；`I_o`、`r[o,i]`（殘餘需求）、`D_o=Σr`。
- `S`：站台；`Cs[s]=6`。
- `P = Pp ∪ Pq ∪ Pb ∪ Pa`；`P^C = Pp∪Pq∪Pb`（committed）。`stock[p,i]`。
- `d(p,s)`、`d(r,p)`：距離（starve-aware 開啟時含飢餓後悔罰項，§4.5）。
- `C=78`（packing，config）；`B_occ`（存活已登記 split 母單數）。
- `releaseLeft(s)`：`GetInfoCurrentPodReleaseLeft()`；`Lead=70`；`T=1`（future pod 目標）。
- `scarcity[i] = min(1, Σ_o r[o,i] / Σ_{p∈P} stock[p,i])`（決策時常數；供給 0 視為 1）。
- `poolDemand[i] = Σ_o r[o,i]`（cpool 用常數）。

### 4.2 變數

| 變數 | 型別 | 意義 | 來源 |
|---|---|---|---|
| `q[i,o,p,s]` | int≥0 | 拆單本體：o 的 SKU i 在 s 從 p 抽幾件 | M2e |
| `xps[p,s]`/`yrp[r,p]` | bin | PS / TA（`P^C` 固定；`Pa` 為決策） | M2e |
| `ysp[o,s]` | bin | o 在 s 佔一槽（part） | M2e |
| `us[s]` | int | 空槽 | M2e |
| `zdone[o]` | bin | 本期整單完成 | M2e |
| `whole[o]` | bin | 單站整單完成、非拆單 | v1 |
| `ypack[o]` | bin | 本期新開 split 母單（佔 packing 一箱） | v1 |
| `ep[o]` | int≥0 | 超出 1 個 station-part 的數量 | v3 |
| `shortfall[s]` | int≥0 | 管線缺口 | v3 |
| `c[i]` | cont≥0 | 被選 pod 集合對 backlog 池的覆蓋量 | **v4** |

### 4.3 限制式

**（A）繼承 M2e（語意不變）**
```
(C1) Σ_o q ≤ stock[p,i]·xps[p,s]      庫存焊死（逐 pod 逐 SKU；聚合檢查的兩次崩潰實證是本條的存在理由）
(C2) ysp ≤ Σ_{i,p} q                  佔槽必有實貨
(C3) Σ_p q ≤ r·ysp                    有貨必佔槽（槽位會計誠實）
(C4) Σ_o ysp = Cs − us                槽守恆（Cs=6）
(C5) Σ_{s,p} q ≤ r                    允許部分；殘量留 backlog（time-split）
(C6) Σ_{s,p} q ≥ r·zdone              完成旗標誠實（獎勵嚴格綁定逐件預留——無 yos 式軟承諾層）
(C7) xps ≤ Σ q          ∀p∈Pa         新趟次必須被消耗（引擎 seed 硬需求）
(C8) pod≤1站、pod需bot、1:1、committed 固定
```

**（B）P1：拆單只綁 committed（v1 核心，含 planning 修正）**
```
(P1a)   whole ≤ zdone
(P1b)   Σ_s ysp + (|S|−1)·whole ≤ |S|        （big-M 形式；草稿 2−Σ 版在 ≥3 站不可行，已修正）
(P1c)   whole = 0                ∀ 既有 parent
(P1oos) whole = 0                ∀ 含缺貨殘量 SKU 的單（否則 decoder split-path 會綁 Pa child——洞已焊死）
(P1d)   Σ_{i,s,p∈Pa} q ≤ D_o·whole           （Pa 只服務 whole）
```

**（C）PK：packing 箱預算（config-gated；per-母單一箱）**
```
(PK1a) ypack + whole ≥ ysp[o,s]   ∀ 新鮮 o, s
(PK1b) ypack ≤ Σ_s ysp            ∀ 新鮮 o
(PK1c) ypack + whole ≤ 1          ∀ 新鮮 o
(PK2)  Σ ypack ≤ max(0, C − B_occ)
```
箱位在 decode 首拆時登記、整併時釋放——比 Xie「第一 part 到站」更早更保守，by construction 不溢出、無 back-pressure 牆。

**（D）D9：多站懲罰線性化**
```
(D9) Σ_s ysp[o,s] ≤ ep[o] + 1     ∀ o（wp>0 時 ep 在最優解取 max(0, parts−1)）
```

**（E）D11：lead-gated pipeline floor**
```
gate[s]（常數）= 1 若 無 Pp 或 releaseLeft(s) ≤ Lead（NaN 視為 >Lead，沿 AE 慣例）
(LG1)  Σ_{p∈Pa} xps[p,s] + shortfall[s] ≥ gate[s]·(T − future(s))    future = |Pq∪Pb at s|
(LGcap) Σ_{p∈Pa} xps[p,s] ≤ max(0, T − future(s))                     防過度供給（AE target=3 反例）
```

**（F）SG：拆單閘門（v4 新增）**
```
sgOpen[s]（常數）= Pp(s) 存在 ∧（strict 模式時再要求 Pq(s)=∅）
(SG1) ysp[o,s] ≤ zdone[o]                    ∀ 新鮮 o、∀ sgOpen[s]=0 的站
(SG2) Σ_{i,s} Σ_{p∈(Pq∪Pb)} q ≤ D_o·zdone[o] ∀ 新鮮 o     （partial 只准抽 Pp——橋接必須立刻可撿）
```
- SG1 白話：閘門關閉的站，新鮮單要嘛本期完成（整單/多站湊齊都算）、要嘛別佔槽。既有 parent 豁免（收尾永遠歡迎）。
- SG2 白話：新鮮單 `zdone=0`（partial）的抽貨來源被夾到只剩 `Pp`（Pa 已被 P1d 夾掉、Pq/Pb 被本條夾掉）。
- **已知 trade-off（probe 裁決）**：strict 模式放棄「Pq 已到但 Pp 還有殘量」的尾段榨取（BackfillProbe：release 殘量 ≈10 件/顆、pivotal 14–19%）——loose 模式即為此而設，兩臂一個布林之差。

### 4.4 目標式（v4）

```
min   w3'·Σ us                                    L1 產出/橋接層（5；自 1000 降級）
    − w2·Σ zdone                                  L2 完成主旋律（40）
    + wp·Σ ep                                     L3 多站懲罰（12；掃 [11,35]）
    + w4·Σ_{p∈Pa} xps                             L4 趟次費（10；經 PodTripFixedCost 既有掛點）
    + w1·[Σ_{Pa} xps·d(p,s) + Σ yrp·d(r,p)]       L5a 距離 tie-break（1）
    + ε·Σ q[i,o,p,s]·scarcity[i]                  L5b 稀缺度加權榨取（ε=−0.5，負=獎勵；替換 flat eps）
    + εcov·Σ c[i]                                 L5c 覆蓋 tie-break（εcov=−0.2，負=獎勵）
         s.t.  c[i] ≤ poolDemand[i]；c[i] ≤ Σ_p stock[p,i]·xps[p,s]（被選 pod 供給）
    + w_pipe·Σ shortfall                          L6 pipeline floor（20）
```

量級紀律：`|εcov|·maxCov` 與 `|ε|·maxUnits` 均 << `w2`；`w3' < w4 < wp < w_pipe < w2`（wp 下界 > 2·w3' 防無謂多佔槽）。**距離成本結構、Pa-only 收費、w4 掛點皆為 M2e 既有程式路徑，只換 xconf 值**；新程式僅 L3/L5b(加權)/L5c/L6。

- **ε 稀缺度安全論證**（不重蹈 eps 臂 TP 623）：P1 下 `zdone=0 ⟹ whole=0 ⟹ Pa 抽貨=0`，SG2 再夾到只剩 `Pp`——ε 唯一能影響的就是「從現場 pod 的 partial 抽多深、抽哪些 SKU」，fishing 派車通道被結構封死。
- **εcov 與 CF 前科的切割**：CF 敗在「字典序 Solve-1 當主菜、且無 P1」；此處是小加法 tie-break 佐料 + P1 結構，同完成數的 pod 集合間才生效。

### 4.5 「最小後悔值」派遣（可選 ablation 臂）

`w4/w1` = 「一趟趟次值幾公尺延遲」的交換率（近雙 pod vs 遠單 pod 例）。開啟 `StarveAwareCostEnabled` 時 `d(p,s)` 內含 `DelayPenalty(到站時間, EST_s)`——到站晚於站台飢餓時點被加罰 = 機會成本的字面模型化（既有機制，鏡像已含）。

### 4.6 為何 order 維度不能拿掉

拆單後 `o` 不再限制 unit 去向，但它是三件事的唯一標籤：(1) 完成計分的 AND 結構（純 SKU 池目標式退化為線性件數 → 「少張大單」confound，已三度證偽）；(2) 槽位量化（6 格槽裝 order-child；Spec 2 聚合鏡像實驗崩潰兩次）；(3) 整併債（packing 以母單計）。**訂單作為「限制」被拆掉，作為「計分/量化/整併單位」保留。**

---

## 5. 機制論證

### 5.0 v1 教訓：`w3=1000` 在拆單世界是毒藥（存檔）

M1G 不能拆單 → 填槽=真產出。拆單開放後填槽有作弊管道：單站可完成的單被故意拆兩站佔兩格（+2000 減免、零新完成、白佔 packing 箱）——「474 次薄承諾×3.1 件」的目標式根源。v3 的 D9+D10 修正。

### 5.1 v3/v4 的榨取節奏（湧現，非規則）

> 新 pod 帶 seed whole 抵達（`Pp`）→ `w2` 吃完成（同時舊供給尾巴被榨）→ `releaseLeft ≤ 70s` → D11 floor 逼出下一顆派工（`Pa→Pb`）→ **successor 行走窗 = SG 閘門開啟窗** → `w3'+ε` 點火 partial 榨乾 `Pp` 殘量（只准抽 Pp、立刻可撿、殘量留 backlog）→ successor 到站（`Pq`）→ 閘門關、回整單主旋律 → 循環

3A/2B 場景在「供給疲弱」前提下逐字重現（§v3 走查保留）；供給健康時完成優先，partial 不搶戲。

### 5.2 M1G 供應穩定機制的繼承對照

M1G 靠：(1) w3 逐站空槽壓力（隱式平衡：pod 流向最餓的站）；(2) 槽位緩衝=隱形管線（等 pod 的佔用槽=在途供給帳面）；(3) 事件驅動重解；(4) `yos` 懸掛層=軟性備貨（pod 按槽外隊列需求加大選型）。IC 繼承 (1)(3)，保留 (2)（whole-seed 停車），把 (4) 的**會蒸發的軟承諾**換成 D11 硬時鐘 + D14 覆蓋項（pod 按殘餘覆蓋選型、但不付未預留的獎勵）。**新風險**：派遣半徑從無限（w3=1000）變有限（≈(w2+w3'−w4)/w1 ≈ 35m）——大陣地校準 watch-out（§10.7）。

### 5.3 PVGS 解剖（行為藍本與其五缺陷）

PVGS-E 每 epoch 四段（`PVGSManager.cs:342-526`，勝出配置 θ=6）：
1. **CompletionSweep**：committed 供給可完全覆蓋的單全部先 commit——完成優先。
2. **DispatchLoop**：價值指數 shortlist、按「新解鎖完成+partial 進度+關親獎勵−距離」派遣、每派一顆重掃（one-claim-resweep）。
3. **PartialSweep**：剩餘槽才填 partial；parents 優先、θ=6 地板、每單每 epoch 一 child。
4. **SqueezeSweep**：最後用**在站 pod** 殘量填殘餘槽（註解原文 "a new trip can never pay for itself at epsilon scale" = P1 的經濟學）。

**五缺陷**（IC 的存在理由）：(a) TP 637 遠低於自身天花板 740——貪婪浪費 ~100 TP headroom；(b) 自認評分錯誤（:536 註解：同時完成的槽位交互 "ignored in the SCORE"）；(c) 價值指數數 item 不數 AND 束；(d) 免費 WIP 補貼（無 packing 上限）；(e) θ=6 是試錯懸崖非連續價格。**IC = 同一套禮拜儀式的聯合最優化版 + 誠實 WIP + 連續定價 + 節奏時鐘。**

---

## 6. 解碼、記帳與觸發

- **解碼**：讀 `q` 落地；whole 快路徑不建 child；否則逐站 `CreateSplitChild`、殘量留 backlog。decode 硬 assert：split-path 單的 Pa 抽貨量必須為 0（P1 ground truth）。
- **packing 記帳**：decode 首拆 `RegisterParent`（idempotent）；整併分支 `ReleaseParent`（引擎唯一掛點，null-safe）；`B_occ = AliveParentCount`。
- **D16 觸發**（引擎，null-safe gated by `Instance.PackingBuffer != null`）：`OutputStation.TakeItemFromPod` 成功撿取後，若 `bot.CurrentTask is ExtractTask` 且 `Requests.Count == 1` → `SignalOrderFinished(null, this)`（僅設 `SituationInvestigated=false`）→ 下次 update 重解，趕在 on-the-fly 窗口關閉前續榨/續派。**case-B（釋槽單=pod 最後工作）結構上救不了**——留 probe 量頻率，損失大再議 release-moment retention（引擎滯留，範圍外）。

---

## 7. 承諾（Commitment）與綁定（Binding）

| | 承諾 | 綁定 |
|---|---|---|
| 變數 | `xps=1, yrp=1`（資源付費） | `ysp=1, q>0`（槽位佔用+逐件預留） |
| 可逆性 | 下期固定（C8） | 每期重解自由 |

三規則：(1) 綁定只准指向已承諾供給（P1d 逆否）；(2) 新承諾只被 whole 綁定觸發（C7+P1d；whole 是「綁定先於供給到位」的唯一例外）；(3) 舊 M2e 的病 = 拆單綁定與新承諾同時發生。

### 7.1 M1G 的承諾誠實性缺口（口試素材，code-verified）

M1G 目標式 `w2·Σyos`（`M1GManager.cs:700`）+ `shi5` 聚合覆蓋（:716-722）= 「可完成訂單」獎勵——但 **`Σyos` 無槽數上限**；decode（:789-799, :851-934）只幫 `yaos=1` 的單做全額 Ziops 預留，`yos`-only 的單**零預留**。範例：1 空槽、o1=2A、o2=3A、pod 5A → 兩單各領 40、pod 按 5A 選型，只有 o1 預留；p 撿完 2A 即釋放（帶 3A 回儲），o2 的承諾蒸發 → 未來重新買趟。物理機會其實充裕（backfill probe：gap 時 pod 在站 97%、可回填 75%）——**是決策層（觸發時機+狀態機盲區）把機會浪費掉**，D16 即為此設。IC 無 yos 層：`C6` 使獎勵嚴格綁定逐件預留。

---

## 8. 架構

### 8.1 Config：`SplitM2eICConfiguration : SplitM1GExactConfiguration`

繼承欄位再用（只換 xconf 值）：`CrossTime`、`OrderRewardWeight`（w2=−40）、`IdleSlotWeight`（**w3'，xconf 必須顯式寫 5**）、`PodTripFixedCost`（w4=10）、`UnitDrawReward`（ε=−0.5，v4 起為稀缺度加權係數）。
新增欄位（宣告順序=序列化順序）：`int PackingBufferCapacity=0`；`double MultiPartPenalty=12`（wp）；`double PipelineFloorWeight=20`；`double PipelineFloorLeadSec=70`；`int PipelineFloorTarget=1`；`bool PipelineFloorEnabled=true`；`bool SplitGateEnabled=true`；`bool SplitGateStrict=true`；`double CoverageRewardWeight=-0.2`（εcov，負=獎勵）。
token：`OBSPLITM2EIC`；enum `SplitM2eIC` + `[XmlInclude]` + `Controller.cs` case。

### 8.2 Manager：`SplitM2eICManager : M1GManager`（鏡像 M2e 新檔，不繼承之）

腳本化拷貝＋改名；ctor 加 `_icConfig` + fail-fast guard（Adaptive/SunkFirst/CF/PR 不支援）+ `PackingBuffer` 掛載；解 MILP 段加：四狀態 partition、P1、SG、D9、D11、εcov cpool、稀缺 ε；decode 加 assert + RegisterParent；決策 log 加 probe 欄。

### 8.3 引擎接觸面（全部 null-safe / 非 IC 完全 inert）

`InstanceCore.cs`（+1 屬性）、`Elements/PackingBuffer.cs`（新元件）、`OutputStation.cs`（整併釋放 1 呼叫 + D16 觸發 1 呼叫，皆 `PackingBuffer != null` 閘控）。

---

## 9. 驗證與實驗

### 9.1 單元測試（pure-function，不碰 Gurobi）

M2eICMath：`PaDrawBigM`（總和/負值拋）、`IsWholeEligible`（P1c/oos）、`PackingBudget`（無限哨兵/clamp）、`SplitOrderDrawsFromStorage`、`MultiPartExcess`（D9 下界）、`PipelineGateOpen`（NaN=關、無 Pp=開、boundary）、`SplitGateOpen`（strict/loose 四象限）、`PoolScarcity`（0 需求=0、0 供給=1、clamp 1）。PackingBuffer：登記/釋放 idempotent、unknown-safe、容量存取。

### 9.2 Ablation 臂（每個 lever 單獨可歸因）

| 臂 | 內容 |
|---|---|
| M2e w3=0 / w3=1000 | 既有基準 + w3 歸因對照 |
| IC-struct | P1 only（v1 權重） |
| IC-v3 | + 五層權重（SG/floor/cov 關） |
| **IC-v4** | + SG(strict) + floor + cov + 稀缺 ε（主臂） |
| IC-v4-gl | SG loose（尾段榨取 ablation） |
| IC-full | + PK=78 |
| PVGS | 對照（後續 plan 補 buffer 臂） |

驗收方向：consolidation wait ↓、pile-on ↑、pod visits ↓、槽駐留 ↓、whole 佔比多數、multi-part 比例受 wp 抑制、starvation gap 受 floor 抑制。`small_o100_mu100`（Cs=6）seed0 煙霧先行，5-seed 待使用者拍板。

### 9.3 Probe（先量後判；決策 log 欄位）

`icWhole`/`icPackOcc`/`icPackBudget`/`icProcUnits`（v1）＋ `icEpSum`、`icShortfallSum`、`icSgOpenCount`、`icGateForegone`（閘門關閉站的 Pp 殘量 × backlog 需求交集——strict 的機會成本直接觀測）、split-share（children vs fastPath 既有欄）。case-B 頻率由 D16 觸發 log 順帶記。權重掃描軸：`wp`、`w3'`、`w_pipe`、`Lead`、`T`、SG 模式。

---

## 10. 風險

1. 供給枯竭：拆單只吃 committed → D2 whole-seed + D11 floor 預防；probe 監測。
2. whole 單早綁 `Pa` 的駐留膨脹：無整併尾巴，次要；監測。
3. **權重整組校準**：xconf 必須顯式覆寫（沿用預設=回 v1 毒藥）；三方對照（M2e w3=0 / w3=1000 / IC）歸因。
4. on-the-fly 窗口：D16 觸發把賽跑提前 ~10s；case-B 仍無解，probe 量損失（上界 14–19% pivotal）。
5. packing per-母單語意（Xie 為準，非 07-15 spec 的 per-child）。
6. 規模：新變數 `O(|O|+|S|+|SKU|)`，同階；solveSec 監測。
7. **派遣半徑有限**（≈35m）：大陣地需重校準或開 starve-aware 出價；small-only 驗證不足以放行大陣地。
8. D9/D11/SG/cov 堆疊交互未驗證：9.2 分層 ablation 逐段歸因，不可只驗 IC-full。
9. SG strict 的尾段榨取損失：`icGateForegone` probe + gl 臂裁決。

---

## 11. 文獻錨點與新意

Xie2021 對應：split-over-time（跨站跨期+殘量留 backlog）、附錄 B C=78、consolidation 零時間、buffer off=其 base 模型、"incomplete orders stay longer in packing"=M2e 尾巴的文獻先例。

**新意（源論文皆無）**：(1) 4D `q` 精確歸屬（承 Spec 3）；(2) P1 入站限定——結構性防手足失聯；(3) 五層權重階層把「完成主旋律、拆單橋接」的管理直覺變成目標式結構；(4) lead-gated pipeline floor 把榨取/派工時序衝突收進聯合最優化；(5) 四狀態 SG 閘門使「拆單窗口=行走窗」；(6) 承諾誠實性對照（M1G yos 軟承諾 vs IC 逐件預留）——方法論貢獻。
