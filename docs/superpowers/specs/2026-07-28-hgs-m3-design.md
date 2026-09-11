# HGS-M3：M3G 的貪婪啟發式對應（可部署、可擴展、解空間 ⊆ M3G）

2026-07-28 · branch `setlevel-redesign` · 作者：Aesop + AI 助理

## 1. 目標與定位

需要一個**可部署、可擴展**的啟發式來處理 **M3G(MILP)跑不動的大案例**——角色等同 online.pdf 中 HADGS 之於 M1G：M3G/MILP 在小案例當離線上限 benchmark，HGS-M3 在大案例當實際跑的方法。

**設計鐵律（使用者定，取代先前失敗的 PVGS-DB 調參路線）**：
- **解空間 ⊆ M3G**：HGS-M3 的每個解都是 M3G-feasible（同決策變數、同可行域、同硬約束）。由建構保證 **M3G 目標值恆 ≥ HGS-M3** → 逼近但不超越，**不靠調參**。
- **貪婪讓結果小輸**：在 M3G 的目標式與可行域上做**逐 epoch 邊際貪婪**，非全域最佳 → 目標值從下方逼近 M3G。
- **公平**：完成/距離/地板/pod-tier 參數與 M3G 逐一相同，唯一差別是「貪婪 vs MILP」。

**先前教訓（PVGS-DB 為何失敗，本設計必須避免）**：PVGS-DB 把 PipelineFloor 當**硬 T=1 序列化閘**（等在途=0 才拉新 pod）→ 過度序列化 → 一台 pod 狂攢（PO 8.5 > M3G 5.82）、平行度低 → TP 崩（237 < 290）。它優化的是**覆蓋**（能塞就塞滿），偏離 M3G 目標式。**修正核心：把 PipelineFloor shortfall 當「軟目標項」放進派遣邊際分數**，讓貪婪主動維持 2-pod pipeline（平行度），而非硬序列化。

## 2. M3G MINCORE 目標式（貪婪忠實優化此式）

最小化：
```
Σ w1·dist(bot→pod→station)·[派新 pod]        w1 = 1
 − |w2|·Σ z[o]                               |w2| = 40；z[o]=1 僅當訂單 o 被完整服務
 + w_pipe·Σ shortfall[s]                      w_pipe = 20
 + pod-tier draw 成本                          α=1 (queued), β=3 (on-the-way)
```
**硬約束**：q 拆單可行性（供給/需求）、站台槽容量、**T cap**（每站在途 pod ≤ T=1 beyond processing，鏡像 M3G icLGcap）。

**關鍵語意**：MINCORE 無 progress reward → 「部分服務」零獎勵，只有**完成整單** +|w2|。拆單動機＝「用多台 pod 的碎片湊齊完成一張單」，非漫無目的堆積。

## 3. 一個 epoch 的貪婪演算法

反覆做「當前邊際最優動作」直到無正邊際動作：

**步驟 1 — 免費完成（completion sweep）**：把所有能被在站/在途 pod 湊齊完成的訂單指派掉，優先抽沉沒 pod（pod-tier α<β）。每筆 +|w2|、~零距離 → 必做。

**步驟 2 — 有益派遣（marginal dispatch）**：在 T cap 允許（站 s 在途 < T）的候選中，對每個 (pod p ∈ 儲區, 站 s) 算忠實邊際分數：
```
Δ(p,s) = |w2|·(p 到站後能新完成的訂單數，可與 s 其他在途 pod 碎片湊齊)
         − w1·dist(bot→p→s)
         + w_pipe·(這台 pod 補上 s 的地板缺額 → 消掉的 shortfall 懲罰)
```
`shortfall[s]` 依 M3G 語意：當 `PipelineGateOpen(hasProcessing, GetInfoCurrentPodReleaseLeft(s), LeadSec=70)` 為真且 `future(s) < T` 時，缺額 = `T − future(s)`；派一台補上即消該懲罰。選 Δ 最大者；**Δ>0 派**並登記湊齊完成的 q 指派，回步驟 1；**Δ≤0 停**。

**三股力如何天然平衡**：
- 完成項 |w2|·z → 驅動 pile-on，但只獎勵「完成」→ 不為攢而攢。
- 地板項 w_pipe·shortfall → 讓貪婪在當前 pod 快撿完時**主動補下一台**，維持 2-pod pipeline = 平行度/吞吐（PVGS-DB 漏此力才過攢）。
- 距離項 + Δ≤0 停止 → 邊際不划算不派。

## 4. 架構

**選項採用：新 manager `GreedyM3GManager`（HGS-M3）**，鏡像 PVGS 已驗證的引擎，改派遣分數為忠實 M3G 邊際（含 shortfall 軟項）。
- **復用 PVGS helper**：快照(InitializePvgs 等價)、epoch state、q 拆單指派(CommitParts 等價)、completion sweep、split planner。以複製/鏡像方式（CLAUDE.md：新模型鏡像、寧可重複，保 ablation 乾淨）。
- **復用 M3G 現成件**：`M2eICMath.PipelineGateOpen`、`OutputStation.GetInfoCurrentPodReleaseLeft`、`Order.CreateSplitChild` + demand ledger、`SplitConsolidationLogger`。
- **新 config**：`GreedyM3GConfiguration : SplitM1GExactConfiguration`（或鏡像 PVGSConfiguration），欄位含 CompletionWeight=40、DistanceWeight=1、PipelineFloorWeight=20、PipelineFloorLeadSec=70、PipelineFloorTarget=1、QueuedPodDrawPenalty=1、OnTheWayPodDrawPenalty=3。引擎需辨識新型別 → 若繼承 `M1GConfiguration` 鏈可免動引擎；否則比照 PVGS 註冊。
- **不動** `M1GManager.cs`/`HADGSManager.cs`/`SplitM2eICManager.cs`/`PVGSManager.cs`（PVGS 維持獨立 baseline）。
- **csproj**：新 `.cs` 手動加 `<Compile Include>`。C# 7.3。

## 5. 複雜度與可擴展性

- 每 epoch：completion sweep O(orders × pods)；dispatch 每輪 O(Pa × stations × orders)，派 ≤ pod 數輪。整體多項式、無 MILP、~ms 級（與 PVGS 同量級）。
- M3G MILP 每決策隨規模超線性 → 大案例爆時間/記憶體。HGS-M3 大案例可跑 = 存在理由。

## 6. 驗收標準

1. **小案例逼近 M3G（核心）**：o100 Fill inv70 2-seed，五指標（TP/PO/IPO/EOR/turnover）由下方逼近 M3G。**PO 不得 overshoot M3G**（overshoot = shortfall 項沒做成軟項、退回 PVGS-DB 病 → 需修）。
2. **公平**：所有共用參數與 M3G 逐一相同（w2=-40, w1=1, w_pipe=20, LeadSec=70, T=1, α/β=1/3），僅貪婪 vs MILP 不同。
3. **可擴展**：一個 M3G 跑不動/極慢的大案例，HGS-M3 決策 ~ms、跑完全程；M3G 同案例超時 → 證明價值。
4. **o200 Fill inv70 不死鎖**。

## 7. 風險與緩解
- **shortfall 項做錯又 overshoot**：驗收步驟 1 的 PO-not-overshoot 是守門;若 overshoot，檢查 shortfall 是否誤成硬序列化。
- **貪婪短視差 M3G >5%**：先如實回報，不加新機制污染「⊆ M3G 貪婪」定位。若差距大，檢查 completion sweep 與 dispatch 順序是否忠實。
- **引擎型別辨識**：若新 config 未被引擎某處 `is XConfiguration` 認得導致行為異常，優先走繼承鏈（如 PVGS 的作法）而非改引擎。

## 8. 驗證計畫
1. Build x64 Release。
2. HGS-M3 o100 2-seed vs M3G 五指標（PO 不 overshoot）。
3. 公平性：diff HGS-M3 config vs M3G 共用參數。
4. 大案例可擴展性：HGS-M3 vs M3G 決策時間 + 能否跑完。
5. o200 死鎖檢查。
6. 指標解析沿用既有 footprint（TP col92, PO col130, IPO col122, turnover col93, EOR statistics.txt KPI_EOR）。
