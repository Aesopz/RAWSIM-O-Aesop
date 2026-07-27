# M3G 登基：set-level 派遣，tier 家族的下一代

2026-07-27 · `split_milp_m3g.xconf` · branch `setlevel-redesign` · 取代 tier(M2e-IC)為預設模型

## 命名脈絡

- **M1G**（Jiao et al. 2025）：整單、不拆，pod→單站。`M1GManager`。
- **M2G**（Xie et al. 2021）：split-over-time，跨站＋跨期部分滿足。
- **M3G**（本研究）：**線上 set-level 拆單**——把派遣正當性從 per-pod 硬閘搬到「該站 inbound pod 集合能完成的訂單數 + 訂單級推進度」目標，並用 pod 分層第 4 層軟成本保住 pile-on。M3G = tier(M2e-IC) 的**死鎖免疫版**。

## M3G = 什麼（相對 tier 的 delta）

M3G 是 `SplitM2eICManager` 開 `SoftInboundCommitted` 主開關的具現：
- **軟化 P1**：移除 icP1d / icSG1 / icSG2 三條硬閘 + decode P1 斷言 → 新車可服 partial（不再「新車必須服整單」）。
- **訂單級線性推進項**（`ProgressRewardWeight`）：每單位服務 earn `-w/D_o`（D_o=原始需求）→ 部分推進有小價值 → 死角時派車做部分推進有理由 → 破死鎖。
- **pod 分層第 4 層 γ**（`NewPodPartialPenalty`）：新車服 partial 最貴（0<α<β<γ）→「偏好沉沒供給」的 pile-on 來源改由軟成本保住,不收斂 M1G。
- **CoverageRewardWeight=0**：訂單級推進項取代 SKU 級覆蓋（統一 w2/cov 概念）。

**config（`split_milp_m3g.xconf`）**：`SoftInboundCommitted=true, ProgressRewardWeight=5, NewPodPartialPenalty=6, CoverageRewardWeight=0`,其餘承襲 tier（w2=-40, w3=1000, w4=0, w2p=0, α/β=1/3, T=1）。`SoftInboundCommitted=false` → 逐位等同 tier（zero-drift 驗證過）。

## 誠實定位（經多輪方法論修正後的最終圖像）

| 場景 | M3G vs tier | M3G vs M1G |
|---|---|---|
| **正常 o100（小 backlog）** | **≈ tier**（打平,無額外好處） | 碾壓（pile-on ~2×、能耗 ~半;bot 稀缺兩者都贏 M1G） |
| **大 backlog o200/o300（Fill, inv70）** | **完勝**——tier 死鎖(凍@2971/2223), M3G 跑滿 ~300-307 TP/h（**~4×**） | 碾壓 |
| **稀疏 SKU（Mu-500）/ 極端 bot 稀缺** | tier 崩/死鎖, M3G 穩健 | — |
| **Fixed 同訂單檔** | ≈ tier | 完勝（既定事實,不再特地驗） |

**一句話**：**M3G 在 tier 不死鎖的正常場景跟 tier 打平,在 tier 會死鎖的場景(大 backlog、稀疏 SKU,即使 70% 庫存)救場、維持 ~300 TP/h。** 它的貢獻是**穩健性/泛用性**(不死鎖),不是正常場景的吞吐碾壓。

## 關鍵證據

- **死鎖治癒(Fill inv70,15 bots)**：tier o200 凍@2971(75.8 TP/h)、o300 凍@2223(56.5)；M3G o200 跑滿(300.5)、o300 跑滿(306.8)。**死鎖非 inv50 產物,inv70 照樣發生,M3G 照樣治。**
- **Fixed(Mu-500,15 bots)9 點掃描**：M3G 全點完勝 M1G,吞吐 +3% vs tier、EOR 更好、pile-on −4%(結構性)。
- **zero-drift**：`SoftInboundCommitted=false` 時 kpi/trips 與 tier 逐位一致。

## 標準評估設定（2026-07-27 起,使用者定案）

- **庫存 70%**（`InitialInventory=0.7`,取代先前的 inv50）。
- **Fill mode**(前進驗證一律 Fill;`small_o{100,200,300}_mu100_4h_inv70.xsett`)。
- **Fixed「拆單≫M1G」為既定事實**,不再特地驗證(其用途=乾淨證明 split>M1G,已完成)。

## 待辦

- M3G 參數(prog=5, γ=6)是 Fixed-Mu-500 選的;宜在 **Fill inv70 標準場景**掃 prog×γ 微調定案。
- 補正常 o100 在 inv70 的三方(M3G/tier/M1G)完整表。
- 分支處理:`setlevel-redesign` 現為 M3G 主線;是否合併/改名由使用者定。
- 統計 bug 修(`DataPoint.cs` FootprintDatapoint,commit 於本分支)宜 port 回 `fill-early-release`。
