# PVGS-DB：貪婪延遲綁定啟發式（快速逼近 M3G 的對照演算法）

2026-07-28 · branch `setlevel-redesign` · 作者：Aesop + AI 助理

## 1. 背景與動機

M3G（`SplitM2eICManager` MINCORE 版）是本研究的先進 MILP 拆單方法。它需要一個**快速的啟發式對照演算法**——如同 online.pdf 中 HADGS 之於 M1G。現有的 PVGS（Pod-Value Greedy Splitting）是拆單的貪婪啟發式，但實測其 pile-on 有**結構性天花板**：

| o100 Fill inv70 (2-seed) | PO | IPO | EOR | TP/h |
|---|---|---|---|---|
| PVGS (greedy) | 3.63 | 7.54 | 1.88 | 311.6 |
| **M3G (MILP target)** | **5.82** | **9.70** | **1.49** | 290.5 |
| M1G | 1.73 | 3.95 | 3.14 | 313.6 |

epsilon（UnitDrawReward）掃描 −0.1→−2.0 證實：PVGS 的 PO 卡在 3.5–4.0、IPO 卡在 7.3–8.3，**參數層調不動**，往深調（−0.5）反而觸發 partial 氾濫崩盤（TP→161）。

**根因（已由 M3G 全塔消融實證）**：M3G 的 pile-on 唯一來源是 `PipelineFloor` 延遲綁定機制。PVGS 是逐 epoch 貪婪派遣（一有覆蓋價值就拉新 pod），缺少 PipelineFloor 那個「時鐘驅動、延後拉新 pod、讓訂單先累積」的動作。

**目標**：把 PipelineFloor 的延遲綁定機制忠實移植進 PVGS 的貪婪引擎，讓 pile-on/IPO/EOR 逼近 M3G 到 **−2% 以內**（小輸），同時保住貪婪的速度（無 Gurobi、~1ms/決策）與 TP/完成時長優勢。

## 2. PipelineFloor 機制解析（M3G 的實作，`SplitM2eICManager.cs:819-859`）

不是補貨量機制，是**時鐘驅動的新 pod 派遣閘門**：
- 每站目標 `T=1`（pipeline 中該有幾台在途 pod 補位）、前置 `LeadSec=70`。
- 閘門 `M2eICMath.PipelineGateOpen(hasProcessingPod, releaseLeftSec, leadSec)`（`M2eICMath.cs:130`）：
  - **無 processing pod → 恆開**（空站一定餵得到，天生防死鎖）。
  - 有 processing pod → 僅當 `releaseLeftSec ≤ leadSec`（當前 pod 剩 ≤70s 工作量）才開。
- 閘開時軟約束逼「派出 T−(在途) 台新 pod」補位；閘關不派。派遣是**時鐘驅動而非覆蓋驅動**，committed pod 因此有累積窗口 → pile-on 攢高。

## 3. PVGS-DB 設計

### 3.1 核心機制：閘控 DispatchLoop 的新 pod 拉取

在 `PVGSManager.DispatchLoop`（`PVGSManager.cs:564`）的 per-station 評估迴圈（line 620 `for (int s ...)`）內，對每個候選 station `s` 加一道閘：當 `DelayedBindingFloor` 開啟時，**只有下列皆成立才允許把新 Pa pod 派給站 s**：
1. `M2eICMath.PipelineGateOpen(hasProcessingPod[s], st.StationList[s].GetInfoCurrentPodReleaseLeft(), _config.PipelineFloorLeadSec)` 為真；
2. 該站在途 pod 數 `< _config.PipelineFloorTarget`（anti-oversupply cap，鏡像 M3G 的 icLGcap）。

閘關 → `continue`（該 epoch 不把新 pod 派給站 s）。

**累積是隱式的**：閘關時覆蓋不到 inbound pod 的訂單留在 backlog 等（PVGS 的 CompletionSweep/PartialSweep 仍會把單填到已在途 pod 上）；閘開拉到新 pod 時，該 pod 一次服務累積的那批可覆蓋訂單 → pile-on 攢高。**不需要任何跨 epoch 保留狀態**。

### 3.2 重用 M3G 現成組件（零重造）
- `M2eICMath.PipelineGateOpen(...)`：`public static`，直接呼叫。
- `OutputStation.GetInfoCurrentPodReleaseLeft()`：M3G 已用，PVGS 可直接呼叫。
- `hasProcessingPod[s]`：站 s 是否有 pod 正在 pick waypoint 處理——由 PVGS 現有的 inbound/pod 狀態判定（實作時對齊 M3G 的 `icPpByStation` 語意：processing = bot 在該站 pick waypoint）。
- 在途 pod 數：由 `inboundPods[station]` / epoch state 取得。

### 3.3 差別 = 小輸來源
M3G 用 MILP 全域決定「哪些單塞哪台待派 pod」；PVGS-DB 用 coverage-value 逐步貪婪塞。拉 pod 的**時機一模一樣**（同閘門），但塞單的**最優性**貪婪略遜 → pile-on 逼近但結構性略低於 M3G。這正是預期的「小輸」，且是可解釋的（貪婪 vs 全域最佳）。

## 4. 架構（gated zero-drift，對齊 M3G 的 `SoftInboundCommitted` 前例）

### 4.1 Config：`PVGSConfiguration` 新增三欄位
```csharp
/// <summary>(PVGS-DB) Master switch: gate new-pod dispatch behind the pipeline lead gate
/// (delayed binding). Off = whole-block skip, bit-identical to plain PVGS.</summary>
public bool DelayedBindingFloor = false;
/// <summary>(PVGS-DB) Lead seconds: pull the next pod only once the processing pod's
/// remaining work is within this. Mirrors SplitM2eIC PipelineFloorLeadSec (70).</summary>
public double PipelineFloorLeadSec = 70;
/// <summary>(PVGS-DB) Target in-flight pods per station (anti-oversupply cap). Mirrors
/// SplitM2eIC PipelineFloorTarget (1 = two-pod pipeline).</summary>
public int PipelineFloorTarget = 1;
```
注意：`PVGSConfiguration : SplitM1GExactConfiguration`，而 PipelineFloor 欄位在其兄弟類 `SplitM2eICConfiguration` 上，**PVGS 不繼承**，故須自行新增。

### 4.2 Manager：`PVGSManager.DispatchLoop` 加 gated 分支
- `DelayedBindingFloor == false` → 整塊跳過，行為與現行 PVGS **逐位一致**（zero-drift，whole-block skip，非係數歸零）。
- `== true` → 套用 3.1 的閘門。

### 4.3 CLAUDE.md 合規
- PVGSManager 非 M1G/HADGS，可直接改；全部 gated，關掉即原版 PVGS。
- PVGS vs PVGS-DB 本身構成一組乾淨消融（隔離「延遲綁定」的價值），與 M3G 消融同紀律。
- 不動 `M1GManager.cs`/`HADGSManager.cs`/`SplitM2eICManager.cs`。
- Build：x64 Release；C# 7.3；新欄位無需改 csproj（同檔案內）。

### 4.4 命名
工作名 **PVGS-DB**（Delayed Binding）。canonical config：`pvgs_db.xconf`（拷貝 `pvgs_e_eps_tier.xconf` + 開三欄位）。使用者可另定正式名。

## 5. 驗收標準

o100 Fill inv70、2 seeds、與 M3G 同 layout/PP/TA/PS（沿用 `pvgs_e_eps_tier` 的前段，已驗證與 M3G 逐段一致）：
1. **效率逼近**：`PO`、`IPO`、`EOR` 三者各落在 M3G 的 **−2% 以內**（小輸；EOR 因越小越好，指「不超過 M3G 的 1.02×」）。
2. **穩健**：o200 Fill inv70 不死鎖（TP 正常，非凍結）。
3. **zero-drift**：`DelayedBindingFloor=false` 時 kpi/footprint 與現行 `pvgs_e_eps_tier` 逐位一致。
4. **速度**：決策時間仍 ~ms 級（無 Gurobi）。

## 6. 風險與緩解
- **閘過嚴 → 站台飢餓**：空站閘恆開已防基本死鎖；若飢餓過高，`LeadSec` 可調大（提早拉 pod）。掃 LeadSec∈{70,120,180} 找逼近 M3G 的甜蜜點。
- **累積不足、pile-on 到不了 −2%**：貪婪塞單非最優，可能只逼近到 −5~−10%。緩解：（a）`LeadSec` 調參；（b）接受「小輸多一點」並記錄為貪婪的結構極限（仍是有價值的結果）。若怎麼調都差 >5%，回報使用者討論，不強行加機制污染啟發式定位。
- **T>1 的行為**：先固定 T=1（同 M3G）；T 是預留旋鈕。

## 7. 驗證計畫
1. Build x64 Release。
2. zero-drift 檢查：`pvgs_db.xconf` 設 `DelayedBindingFloor=false` vs `pvgs_e_eps_tier` → footprint 逐位一致。
3. 主驗收：`pvgs_db.xconf`（floor on）o100 2-seed vs M3G，檢查 PO/IPO/EOR −2% 內。
4. LeadSec 掃描（若步驟 3 未達標）：{70,120,180}。
5. o200 死鎖檢查。
6. 全程用既有 footprint 解析（TP=col92, PO=col130, IPO=col122, turnover=col93, EOR=statistics.txt KPI_EOR）。
