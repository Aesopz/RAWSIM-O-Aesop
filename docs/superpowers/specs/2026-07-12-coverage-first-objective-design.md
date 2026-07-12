# Coverage-First 目標重構（M2e-CF / PVGS-CF）設計

2026-07-12。前情：PVGS-E 驗收判準二 FAIL-UPWARD（`docs/2026-07-12-pvgse-acceptance.md`）——移除所有可對齊差異後，heuristic 仍以 27~80% 統治效率。診斷：M2e 單層目標中距離與完成獎勵以 40:1 可互換 → 挑「近而稀」pod、覆蓋碎片化；貪婪的覆蓋優先行為恰好接近正確的目標結構。**修法＝修 exact 的目標函數，不是閹割 heuristic。**

## 0. 設計錨點（使用者原話，2026-07-12 定案）

1. 「拆單策略（都指跨期拆單，本身涵蓋跨站）＝放寬 pod 之間供給的限制、station 跟 order 之間的限制。抽象問題：我有一個 backlog demand pool，把 demand 攤開對照 pod supply，**哪些 pod 可以最大程度涵蓋 demand pool？求最大 completed order**。」
2. 「接著才考慮 task allocation——執行層的旅行成本，考量站點與 pod 位置；**針對 travel cost 限制，但不能蓋過 pod 本身對於未來供給的價值**。」
3. 「看的不再是這個 pod 可以完成多少訂單，而是**這個 pod selection 組合集群意味著多少可以被完成的 orders**。travel cost 不能主導決策，而是根據 bot location 輔助派工。」
4. 「**主體並非完整訂單，而是池化後，變成 pod 跟 backlog 為中心主義**；station 跟 pod 只影響 bot 派工；travel cost 決定哪個 pod 送到哪個站點被哪個 bot 服務；**怎麼拆單（拆成幾個單？分配到哪些站？）都根據 pod-station 為中心思考距離成本**。」
5. PoolCover 視野＝**全 backlog**（含本期槽位容不下的需求）——pod 的未來供給價值由此內生。
6. γ 切片折價（order 中心的收官/切片區分）**作廢**——池化中心主義下拆單形態是執行層湧現結果，不進獎勵結構。

## 1. 形式模型：字典序兩段求解

**限制式一條不動**（elink1/2/3、eshi4-13'、eM2/eM2done 全保留，見 `docs/2026-07-11-m2e-model-definition.md`）。只換目標，以兩次 Gurobi 求解實現字典序：

### Solve 1（供給覆蓋層——決定「取哪些 pod、完成哪些單」）

$$\max\;\; W_z\sum_o z_o \;+\; \beta \sum_i c_i \;-\; \varepsilon_S \sum_{p\in\mathcal{P}_a}\sum_s x_{p,s}$$

- $z_o$＝既有 zdonex（可見殘量全數指派）。$W_z = 1$（尺度錨）。
- **PoolCover 線性化**：每 SKU 新增連續變數 $c_i \ge 0$：
  $$c_i \le \mathrm{PoolDemand}_i, \qquad c_i \le \sum_{p} k_{p,i}\cdot u_p, \qquad u_p = \sum_s x_{p,s}\;(\text{Pb 恆為 1})$$
  $\mathrm{PoolDemand}_i$＝**第一階段准入訂單集**（Od 縮池前）的殘量總和——「全 backlog」語意；縮池只影響誰能佔槽，不影響池的定義。
- $\beta$（PoolCoverWeight）：**次於完成、高於雜訊**。校準原則 $\beta\cdot\max_i\mathrm{PoolDemand}_i < W_z$（單 SKU 的池覆蓋再多也換不走一張完成單）；default 0.005，掃 {0.002, 0.005, 0.01}。
- $\varepsilon_S$（PodSelectTiebreakCost）：pod visits 最少化的字典序層，壓在 β 之下：$\varepsilon_S <$ β·(一個 pod 的典型覆蓋量)；default 0.01，掃 {0.005, 0.01, 0.05}。
- **沒有任何距離項。** 記錄最優值 $Z^*$（完成加權和）、$C^*$（池覆蓋）、$T^*$（新趟數）。

### Solve 2（執行層——決定「pod 去哪站、哪台 bot 接」）

$$\min\;\; \sum_{p\in\mathcal{P}_a,s} d_{p,s}\,x_{p,s} + \sum_{r,p} d^{RP}_{r,p}\,y_{r,p}$$

s.t. 原限制式 ＋ 三條鎖層約束：
$$W_z\Sigma z + \beta\Sigma c \ge W_z Z^{*}_{z} + \beta C^{*} \;(\text{以合併值鎖，容差 1e-6}), \qquad \sum_{p\in\mathcal{P}_a,s} x_{p,s} \le T^{*}$$

- 距離**只在 L1 最優解集合內部**裁決——「travel cost 輔助派工、不主導」。
- 拆單形態（幾個 child、落哪些站）在此湧現：q 的站別分佈由距離成本決定＝「怎麼拆根據 pod-station 距離成本思考」。
- 選配防病態閥（大陣地才需要）：$d_{p,s}\,x_{p,s} \le D_{\max}$ 進 Solve 1 當可行性約束（travel 以「限制」而非「成本」進頂層——錨點 2 的字面實作）。small 上距離有界（<60m），default 不啟用。

### 落地

Solve 2 的解走既有管線（SplitMilpDecoder → children → Ziops → bot 認領），零改動。

## 2. PVGS-CF：heuristic 的忠實鏡像

PVGS-E（ExactAlignedScoring）加讀同一組新欄位，E 模式派遣分數改字典序比較（tuple 比較，不加權混合）：

```
candidate 排序鍵 = ( W_z·N + β·podPoolCoverGain − ε_S ,  −(dBot+dPod) )
主鍵 > 0 才認領；主鍵相同時距離小者勝
```

- `podPoolCoverGain` ＝ 該 pod 對 $\Sigma c_i$ 的邊際增量（現有 PvgsValueIndex 的計算骨架直接改造——它本來就是稀缺加權殘量覆蓋）。
- 兩邊讀**同一個 config**（欄位在 SplitM1GExactConfiguration，PVGSConfiguration 繼承）→ 同目標 by construction。
- 貪婪仍是單 pod 邊際、認領後重掃——這是它該輸的地方；現在 exact 用真正的集合協同對打同一個目標，統治驗收的公平戰場。

## 3. Config

`SplitM1GExactConfiguration` 新增（default 全部＝關閉/bit-identical）：

| 欄位 | 型別 | default | 意義 |
|---|---|---|---|
| `CoverageFirstScoring` | bool | false | 啟用字典序兩段求解（M2e 側）；PVGS-E 模式下同時切換其評分 |
| `PoolCoverWeight` | double | 0.005 | β |
| `PodSelectTiebreakCost` | double | 0.01 | ε_S |

xconf：`sweep/cf_<β>_<εS>.xconf`（M2e 側）、`pvgs_cf.xconf`（PVGS 側）。既有檔案照憲法不動。

## 4. 驗收（沿用使用者 2026-07-12 修訂版三臂鏈判準）

小掃描（β×ε_S，seed 0/1）選定操作點後，三臂 5 seeds（small 7200s）：

1. **M2e-CF ≥ M1G**（六項）＝拆單純淨收益（預期輕鬆過——舊 M2e 已過）。
2. **PVGS-CF 五項（TP/PO/RD/OD/EOR）全劣於 M2e-CF 且各項差距 ≤ ~5%**（輸而不崩）。
3. **內部合理性**：M2e-CF 對舊 M2e(w4=0) 應為「TP 損失 ≤ ~2% 且效率大幅改善」（PO 期望進 3.x 帶、RD 期望 ≤ 15k）——否則重構本身失敗，先於判準二報告。
4. 回歸：三旗標全關 → M2e 643（默認 xconf）/ sw_0_0 重跑一致；PVGS-E 舊模式不受影響（其分支只在 CoverageFirstScoring=true 時改變）。
5. 速度：兩段求解 decisionSec 中位數記錄（預期 <0.1s；若尾部惡化為 exact/heuristic 分工加分證據）。

## 5. 風險

- **取供病態**：L1 無距離，β·cover > ε_S 時「純供給趟」成立——bot 可能被派去接「本期零完成、純池覆蓋」的遠 pod。這是設計**有意允許**的（未來供給價值），由 ε_S 校準界線；掃描如見 TP 崩，第一動作是升 ε_S。station 佇列容量是天然的第二道閥。
- **兩段求解時間**：×2 Gurobi 呼叫；small 無虞，大陣地留意 Solve 1（無距離的覆蓋問題可能更難分支）。
- **鎖層數值容差**：合併值鎖（$W_z Z + \beta C$）避免逐層鎖的多次求解；1e-6 相對容差防數值抖動割掉真最優。
- **PoolDemand 用縮池前集合**：與模型內 pendingOrders（縮池後）不一致是刻意的（池≠佔槽資格）；實作時兩個集合都已在 InitializeSplitExact 內可得（pendingOrders1 vs pendingOrders）。
- **閉環仍可能反轉**：coverage-first 內生化了 thickness 的大部分，但貪婪的逐步節奏仍在。若 PVGS-CF 再度反超——那將是「連目標對齊都擋不住序列節奏」的終極證據，屆時判準二的重框（per-epoch 層陳述）成為唯一誠實選項。

## 6. 被取代的設計（記錄）

- γ 切片折價（收官/切片獎勵區分）：order 中心概念，與池化中心主義不相容，作廢。其診斷出的「重複收割」扭曲在 CF 下自然消解——完成獎勵仍逐期發放，但 pod 選擇由池覆蓋主導，切片不再是 solver 的套利對象（無距離套利空間）。
- PVGS-E2（非自適應貪婪）：擱置——CF 若成功則不需要；CF 若失敗它仍是備案。

## 7. 相關文件

- 驗收失敗報告：`docs/2026-07-12-pvgse-acceptance.md`（結構診斷＝本設計的動機）
- PVGS-E spec：`docs/superpowers/specs/2026-07-12-pvgs-e-design.md`
- M2e 模型定義：`docs/2026-07-11-m2e-model-definition.md`
- 代碼錨點：`SplitM1GExactManager.cs` SolveSplitExact（目標與求解）、`PVGSManager.cs` DispatchLoop（E 評分）、`PvgsValueIndex.cs`（池覆蓋骨架）
