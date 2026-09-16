# HADGS 運作說明

本文依據目前 checkout 的程式碼整理，主要來源是：

- `RAWSimO.Core/Control/Defaults/OrderBatching/HADGSManager.cs`
- `RAWSimO.Core/Control/OrderManager.cs`
- `RAWSimO.Core/Control/Controller.cs`
- `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`
- `Material/Instances/CoreBenchmark/large/PPWHCAnStar-TABalanced-SAActivateAll-ISEmptiest-PSNearest-RPDummy-OBHADGS-RBSamePod-MMNoChange.xconf`

## 一句話版本

HADGS 是一個 order batching manager。它在有站點容量可以補、或有新訂單/完成訂單等事件讓狀態變動時，為 output station 選 order，並為站點補 inbound pod，再把 pod 指派給 bot。

它的核心流程不是全域最佳化整個 POA/PPS/TA，而是：

1. 先用目前已經 inbound 到各站的 pods，盡量把可以完成的 orders 指派給站點。
2. 如果站點還有容量，再從 unused pods 中找一個或一組 pod 補進該站。
3. 補 pod 時用 scorer 評估「這些 pod 進站後可以完成多少 pending orders」與「bot-pod、pod-station 距離」。
4. 單 pod 情況用最近 bot；多 pod fallback 情況用 Gurobi assignment model 最小化 bot 到 pod 的總距離。

## 啟用入口

HADGS 由 controller config 的 `OrderBatchingConfig` 決定。

在 `Controller.cs` 裡，當 `OrderBatchingConfig.GetMethodType()` 回傳 `OrderBatchingMethodType.HADGS` 時，會建立：

```csharp
OrderManager = new HADGSManager(instance);
```

對應 config class 是 `HADGSConfiguration`。它的主要參數包含：

- `TieBreaker`: 預設 `EarliestDueTime`
- `FastLane`: 預設 `true`
- `LateBeforeMatch`: 預設 `false`
- `FastLaneTieBreaker`: 預設 `EarliestDueTime`
- `UseBAED`: 預設 `false`
- `BAEDReferenceSpeed`: 預設 `1.5`
- `UseReturnPendingBots`: 預設 `false`
- `ReturnPendingDistanceThreshold`: 預設 `1.0`

large benchmark 的 HADGS xconf 使用：

```xml
<OrderBatchingConfig xsi:type="HADGSConfiguration">
  <TieBreaker>EarliestDueTime</TieBreaker>
  <FastLane>true</FastLane>
  <LateBeforeMatch>false</LateBeforeMatch>
  <FastLaneTieBreaker>EarliestDueTime</FastLaneTieBreaker>
  <UseBAED>false</UseBAED>
  <BAEDReferenceSpeed>1.5</BAEDReferenceSpeed>
  <UseReturnPendingBots>false</UseReturnPendingBots>
  <ReturnPendingDistanceThreshold>1</ReturnPendingDistanceThreshold>
</OrderBatchingConfig>
```

## 何時會求解

HADGS 不是每個 tick 都無條件完整求解一次。

`OrderManager.Update()` 會先從 `ItemManager` 撈新訂單，把新訂單加入 `_pendingOrders`，並把 `SituationInvestigated` 設成 `false`。訂單完成、新訂單、bundle stored、station activated 也會讓 `SituationInvestigated = false`。

接著若目前 order batching config 是 `HADGSConfiguration` 或其子類，會：

1. 呼叫 `GenerateCs()` 更新每個 output station 的剩餘容量：
   `Cs[station] = station.Capacity - station.CapacityReserved - station.CapacityInUse`
2. 若 `!SituationInvestigated` 或任一站點 `Cs > threshold - 1`，進入 order batching 判斷。
3. 只有當任一站點 `Cs > threshold - 1` 時，才真的呼叫 `DecideAboutPendingOrders()`。
4. 最後把 `SituationInvestigated = true`。

這代表 HADGS 的實際 batching work 主要發生在站點還有可補容量時；只是 HADGS 分支比一般 OB 多了「即使狀態已 investigated，只要容量條件成立仍可再次檢查」的 gate。

## DecideAboutPendingOrders 主流程

`HADGSManager.DecideAboutPendingOrders()` 是主入口，流程如下。

### 1. 初始化 scorer

第一次進入時呼叫 `Initialize()`。

初始化會建立 normal order selector：

- 若 `LateBeforeMatch = true`，先加一層 late order scorer。
- 主要 scorer 會看該 order 在目前 station 的 inbound pods 上需要哪些 pod 才能滿足 demand，並回傳這些 pod 到 station 的距離總和。
- tie breaker 依 `TieBreaker`，預設是 earliest due time。

HADGS 也會建立 `_bestPodOStationCandidateSelector`。在 large config 裡，它來自 TaskAllocation 的 `PodSelectionConfig.OutputPodScorer` 與 `OutputPodScorerTieBreaker1`：

- 第一層通常是 `PCScorerPodForOStationBotCompleteable`
- tie breaker 通常是 `PCScorerPodForOStationBotWorkAmount`

在 HADGS 裡，`Completeable` scorer 最終會呼叫 `Score()`。

### 2. 記錄 decision-space KPI

每次 trigger 會記錄：

- 每站 inbound pod queue depth
- 全站可用 slots
- unused pods 數量
- pending orders 數量
- `slots * unusedPods * pendingOrders` 作為 candidate combos proxy
- idle/rest bots 數量

這些統計就是後續觀察 large HADGS 爆量時的重要線索。

### 3. 重建每站 inbound pod snapshot

HADGS 先清空 `_inboundPodsPerStation`，再把每個 output station 目前的 `InboundPods` 複製進來。

如果發現某個 inbound pod 沒有被 `BottoPod` 綁定，且其 `_usedPods[pod].CurrentTask` 是 `RestTask`，會釋放該 pod 並從 station unregister。這是在清理看起來已經不該再算作 inbound 的 pod。

### 4. 產生 Od：快到期且庫存可滿足的訂單集合

`GenerateOd(_pendingOrders)` 會對每個 pending order 計算：

```text
Timestay = DueTime - (current wall-clock-ish sim time - TimePlaced)
```

然後依 `Timestay`、`DueTime` 排序，設定 `order.sequence`。

Od 只包含：

- 全部 order positions 都能由目前 unused pods 的可用庫存滿足
- `order.Timestay < DueTimeOrderofMP`

目前 `DueTimeOrderofMP` 是 30 分鐘。

### 5. 決定要用全部 pending orders 還是 Od

如果 `Od.Count == 0`，或 `Od.Count < Cs.Sum(v => v.Value)`，HADGS 直接用全部 `_pendingOrders` 做 `HeuristicsPOAandPPS()`。

否則，HADGS 暫時把 `_pendingOrders` 換成 `Od`，優先處理快到期訂單；跑完後再把 `_pendingOrders` 還原。

## HeuristicsPOAandPPS：POA + PPS 主體

`HeuristicsPOAandPPS()` 是 HADGS 的核心熱路徑。

### Station 順序

它掃過 `Instance.OutputStations`，只處理目前可 assign 的 station。

若 `SettingConfig.StarveAwareCostEnabled` 開啟，station 會依 `StarveAwareCost.Est(station, currentTime)` 排序。否則 key 是常數，保留原本 output station 順序。

注意：這裡只有排序站點處理順序，不代表 HADGS 本身有完整 starvation-aware objective。

### POA：先用既有 inbound pods 填 order

對每個 station，HADGS 先嘗試把「目前 inbound pods 已經能完成」的 orders 指派給該 station。

條件是：

```text
order 的每個 SKU demand <= 該 station inbound pods 對該 SKU 的可用數量總和
```

找到候選 order 後，用 `_bestCandidateSelectNormal` 選最好者。選中後：

1. `AllocateOrder(chosenOrder, chosenStation)`
2. 從 `_pendingOrders1` 移除
3. 依 pod 到 station 的距離排序 inbound pods
4. 對 order 的 extract requests 找 fitting requests
5. 對 pod 呼叫 `JustRegisterItem()`
6. 把 pod 到 fitting requests 的關係寫入 `Instance.ResourceManager._Ziops1[station]`

這一段是「POA」：把 order 派到 output station，並把需求映射到 station inbound pods。

### PPS：若站點還有容量，補 pod

當 station 還可 assign 且還有 pending orders 時，HADGS 會找可用 bot：

```text
Instance._outputstationbots 中：
- bot.Pod == null
- bot 不在 _usedPods 的 value
- bot 不在 ResourceManager.BottoPod 的 key
```

如果 `UseReturnPendingBots = true`，接近歸還 storage location 的 park-pod bot 也可能被納入；baseline large config 預設是 false。

接著掃 `Instance.ResourceManager.UnusedPods`，只看：

- 該 pod 至少能提供 pending orders 中某個需求 item
- pod 沒有在本輪 `SelectedPod`
- pod 沒有已經被 `BottoPod` 使用

對每個候選 pod，HADGS 暫時把它放進 `_inboundPodsPerStation[station]`，選最近 bot，並用 `_bestPodOStationCandidateSelector.Reassess()` 評估。最後若有 `BestPod`：

1. 把 pod 加入 station inbound snapshot
2. `SelectedPod.Add(BestPod)`
3. `station.RegisterInboundPod(BestPod)`
4. `ResourceManager.BottoPod.Add(BestRobot, BestPod)`
5. `ResourceManager.ClaimPod(BestPod, BestRobot, BotTaskType.Extract)`
6. `goto L` 回到同一 station，再嘗試用新 inbound pod 做 POA

這就是 HADGS 的主要循環：補 pod 後，馬上重新嘗試用該站 inbound pods 完成更多 orders。

## Score：候選 pod 的評分

HADGS 補 pod 的重要 scorer 是 `Score()`。

它會把目前 station inbound pods 的所有可用 SKU 數量加總，然後掃 `_pendingOrders`，計算這些 pods 最多能讓多少 pending orders 完整滿足。

若 `completeableAssignedOrders == 0`，分數是 `double.MaxValue`。

否則分數是：

```text
score = -(40 * completeableAssignedOrders)
        + sum(bot -> pod estimated distance + pod -> station estimated distance)
```

HADGS 的 selector 是 minimization，所以：

- 可完成訂單數越多越好，因為 `-40 * completeableAssignedOrders` 會讓 score 變小。
- 距離越短越好。
- `40` 是寫死的完成訂單 reward 權重。

這裡沒有直接用 ETA deadline feasibility，也沒有直接用 station starvation gap 作 objective；它主要是「完成更多 order + 少走距離」。

## SimplePOAandPPS：單 pod 找不到時的 fallback

如果單 pod PPS 找不到 `BestPod`，而可用 bot 超過一台，HADGS 會進入 `SimplePOAandPPS()`。

這段做的是較重的組合搜尋：

1. 從 pending orders 中找庫存可滿足的 orders。
2. 依 `order.sequence` 優先處理，最多看 `LocalSearch = 3` 個 orders。
3. 對該 order 的每個 SKU 呼叫 `GeneratePiSKU()`。
4. `GeneratePiSKU()` 會找出能滿足該 SKU 數量需求的最小 pod 組合。
5. 將各 SKU 的 pod 組合做 Cartesian product，得到能完成整張 order 的 pod set。
6. 過濾掉需要 pod 數大於可用 bot 數的組合。
7. 隨機抽最多 10 個 pod set 嘗試。
8. 每個 pod set 用 `SolveByMp(Gurobi, pods, station, Ra)` 求 bot-pod assignment。
9. 再用 `_bestPodOStationCandidateSelector.Reassess()` 評估這組 pod 對 station 的價值。

若找到最佳 pod-to-bot 組合，會逐一 commit：

- 從 `Ra` 移除 bot
- 把 pod 加到 `_inboundPodsPerStation[station]`
- `SelectedPod.Add(pod)`
- `station.RegisterInboundPod(pod)`
- `ResourceManager.BottoPod.Add(bot, pod)`
- `ResourceManager.ClaimPod(pod, bot, BotTaskType.Extract)`

然後減少 local station capacity counter `cs--`，繼續下一張 order。

## SolveByMp：bot-pod assignment model

`SolveByMp()` 建立一個 binary linear model：

```text
yrp[robot, pod] in {0, 1}
```

目標：

```text
minimize sum(yrp[robot,pod] * EstimateBotPodDistance(robot,pod))
```

限制：

- 每個 pod 必須剛好由一台 robot 運送。
- 每台 robot 最多分配一個 pod。

求解成功後，將解中的 pod 加入 `_inboundPodsPerStation[station]`，並寫入 `CurrentPodtoBot`。

所以這個 MP 只解「這組 pod 要由哪些 bot 搬」；它不是同時最佳化 order、station、pod、bot 的完整聯合模型。

## 距離與 BAED

HADGS 的距離估計有兩段：

- `EstimateBotPodDistance(bot, pod)`
- `EstimatePodStationDistance(pod, station)`

baseline 主要用 Manhattan 或預先快取的 shortest-path-like distance。

如果 `UseBAED = true`，會加入 blocking-aware effective distance：

```text
effective distance = physical distance + BAEDReferenceSpeed * entry delay seconds
```

large HADGS config 目前 `UseBAED=false`，所以 baseline HADGS 不使用 BAED。

## 與 TA / path planning 的關係

HADGS 本身做的是 order batching 和 pod claim。

它會透過：

- `station.RegisterInboundPod(pod)`
- `ResourceManager.BottoPod.Add(bot, pod)`
- `ResourceManager.ClaimPod(pod, bot, BotTaskType.Extract)`

把 pod/bot 任務交給後續 task allocation / path planning 流程。

也就是說，HADGS 裡的 bot-pod 選擇只決定「哪個 bot 去拿哪個 pod」，後續實際路徑與擁塞處理仍由 controller 中的 TA/PP 組件處理。

## 為什麼 large 會慢

目前 HADGS 的 large 慢點主要在 order batching candidate explosion，不是單純 path planning。

程式上有幾個來源：

1. 主流程每次 trigger 都可能掃站點、pending orders、unused pods。
2. `Score()` 每次評估 pod candidate 時會再掃 `_pendingOrders`，計算 completeable orders。
3. fallback `SimplePOAandPPS()` 會對每個 SKU 產生 pod 組合，並把多 SKU 組合做 Cartesian product。
4. 每個抽到的 pod set 還會呼叫 Gurobi 解 bot-pod assignment。
5. large 場景下 pending orders、unused pods、station slots 都大時，候選空間會快速放大。

目前程式也有統計：

```text
candidate combos proxy = slots * unusedPods * pendingOrders
```

這個值可以用來判斷 HADGS 是否進入爆量區間。

## 目前實作上的重要限制

以下是依目前程式碼可確認的限制：

1. HADGS 不是以 station starvation 為主 objective。
   它最多在 `StarveAwareCostEnabled` 時改變 station 掃描順序；核心 score 仍是 completeable orders 與距離。

2. HADGS 不是以 ETA deadline feasibility 為主 objective。
   它會用 due time/timestay 產生 Od，但 pod/bot/station 配對沒有檢查「此任務能否在時間窗內到達」。

3. `Score()` 的完成訂單 reward 權重 `40` 是硬編碼。
   這讓完成更多 orders 和走更短距離之間的 tradeoff 固定，沒有從 config 控制。

4. `SimplePOAandPPS()` 有隨機抽樣。
   它從 candidate pod sets 中隨機取最多 10 組試，因此結果可能受 seed / 隨機順序影響。

5. Gurobi model 只處理 bot-pod assignment。
   它不解完整 POA/PPS/TA 聯合最佳化。

6. 若 large 場景候選空間太大，order batching 會成為瓶頸。
   這也是前面 1800 秒 large SAHADGS/HADGS 類實驗容易卡在早期的原因之一。

## 簡化流程圖

```text
Controller 建立 HADGSManager
        |
OrderManager.Update()
        |
新訂單/完成/容量 gate 觸發
        |
GenerateCs()
        |
DecideAboutPendingOrders()
        |
初始化 scorer、記錄 decision KPI、重建 inbound pod snapshot
        |
GenerateOd()
        |
HeuristicsPOAandPPS()
        |
for station with capacity:
        |
  POA: 用既有 inbound pods 指派可完成 orders
        |
  PPS: 若還有容量，找 unused pod + available bot
        |
    成功: RegisterInboundPod + ClaimPod，回到 POA
        |
    單 pod 失敗: SimplePOAandPPS()
        |
      GeneratePiSKU -> pod set combinations
        |
      SolveByMp(Gurobi) -> bot-pod assignment
        |
      RegisterInboundPod + ClaimPod
```

