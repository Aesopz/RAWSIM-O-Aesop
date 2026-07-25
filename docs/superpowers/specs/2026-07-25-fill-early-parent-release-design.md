# Fill 模式 tier 母單提早釋出(EarlyParentRelease)設計

2026-07-25 · branch 待定(建議從 `tier-current` 開新分支) · 作者 Aesop + Claude

## 1. 動機與問題

論文比較 M1G vs tier(M2e-IC)吞吐時,參考論文(online.pdf / split.pdf)使用 **Fill 模式**——`_availableOrders` 訂單池維持在 `OrderCount`(=100),某訂單離開池子就補一張新鮮單。

**觸發補單的是「訂單離開 `_availableOrders`」,不是「訂單完成」**(`ItemManager.cs`:Fill update `while (_availableOrders.Count < OrderCount)`;`TakeAvailableOrder` 把訂單從 available 移到 open)。

- **M1G**:整單指派 → `TakeAvailableOrder` → 池子少一 → 立即補新鮮單。吸單快。
- **tier**:拆單後,母單目前**只在 `IsFullyClaimed`(所有單位都拆成 child)才**被 `TakeAvailableOrder` 移出池子(`SplitM2eICManager.cs:1762-1771`)。部分拆分、還有跨期殘餘的母單會**長期卡在 `_availableOrders` 佔住 OrderCount 名額** → Fill 不補新單 → tier 訂單流被人為壓低 → Fill 模式下吞吐虛低,M1G 虛高。

這是 Fill 模式對拆單的結構性不公平。已知 Fixed 模式(固定訂單檔)下 tier 已勝 M1G(items +12%、能耗省 42%,見 `project_m2e_ic.md`);本設計是把 Fill 模式的人為壓制也拿掉,讓 tier 在**兩種模式**都成立、對齊參考論文的原生設定。

## 2. 目標

Fill 模式下,讓 tier 的每次「首次拆單」如同 M1G 的「整單指派」一樣釋出一個補單名額,使 tier 的訂單流不再被殘餘母單堵塞——讓 tier 的 T=1 吞吐真實勝過 M1G。

## 3. 核心機制:解耦「補單閘門」與「可服務性」

`_availableOrders`(Fill 補單閘門)與 `_pendingOrders`(tier 決策工作集)是**兩個獨立集合**,目前都綁在 `IsFullyClaimed`。本設計解耦它們:

| 事件 | 現況(flag off) | 新行為(flag on) |
|---|---|---|
| 母單**第一次拆分**(`IsSplitParent` 由 false→true) | 不動 | `TakeAvailableOrder(parent)` 移出 `_availableOrders` → Fill 補一張新鮮單。**只釋出一次**(guard:仍在 available 才做) |
| 母單留在 `_pendingOrders` | 直到 `IsFullyClaimed` | **不變**(直到 `IsFullyClaimed`)→ tier 繼續跨期服務殘餘 |
| 母單記為完成 | 只靠 consolidation | **不變**——`TakeAvailableOrder` 純移動 available→open,不觸發任何 handled/completed 計數;母單仍只在所有 child consolidation 完成時 `CompleteOrder(parent)` |
| `IsFullyClaimed` 時 | `_pendingOrders.Remove` + `TakeAvailableOrder` | `_pendingOrders.Remove`(+ `TakeAvailableOrder` 對已釋出者為 no-op) |

**M1G 完全不動**(憲法要求「絕不動 M1GManager.cs」;且 M1G 本來就整單釋出、吸單快,無需改)。修改純 tier 側。

### 3.1 正確性依據(已讀碼驗證)

1. **不會提前計完成**:`Order.NotifyChildCompleted` 回傳 `IsFullyClaimed && _completedChildren.Count >= _children.Count`(`Order.cs:302`)。母單未完全拆完 → `IsFullyClaimed` 為 false → 即使當前 children 全完成也**不會** consolidation。提早釋出不影響完成時機。
2. **`_openOrders` 只是帳目**:`TakeAvailableOrder` 把母單加進 `_openOrders`;全庫僅 `GetInfoOpenOrders`(純資訊)讀它,無任何邏輯假設 `_openOrders` 內訂單已指派站台。提早進 open 集只是延長窗口,`CompleteOrder(parent)` 仍在 consolidation 時移出。安全。
3. **只釋出一次**:guard `ItemManager.IsOrderAvailable(parent)`(O(1) HashSet.Contains)——釋出後即 false,同一母單後續回合再拆不會重複釋出、不會過度補單。

## 4. 公平性立場(使用者定案)

「首次拆一件即釋出一名額」使 tier 比 M1G(整單多件才釋出一名額)更快吸入新鮮單,`_pendingOrders` 殘餘可能累積。**使用者裁定:接受此為拆單的正當優勢**——tier 靠「少量多次拆、共享 pod 湊單」提高 pile-on,快速吸單讓它能同時掌握更多可湊訂單,是真實效益,不是造假。不做速率匹配防禦。

口試防禦敘事:Fixed 模式(完全對稱訂單檔)已獨立證明 tier 勝出;Fill 模式此修法只是移除對拆單的人為壓制,回到與 M1G 對等的「消耗一格→補一格」節奏。

## 5. 實作(逐字)

### 5.1 Config flag（`MethodConfigurationsOB.cs`,`SplitM2eICConfiguration` 內,`SplitCanDriveDispatch` 附近）

```csharp
        /// <summary>(Fill fairness) When true, a split parent is released from the ItemManager's
        /// available-order backlog on its FIRST split - freeing a Fill replenishment slot so a
        /// fresh order is injected at the same cadence M1G gets from whole-order assignment -
        /// while staying in _pendingOrders to serve its residual across periods. It is NOT marked
        /// complete; the parent completes only via child consolidation. Default false = release
        /// only at IsFullyClaimed (current behavior, bit-identical). Only affects Fill mode.</summary>
        public bool ReleaseParentOnFirstSplit = false;
```

### 5.2 O(1) availability accessor（`ItemManager.cs`,`TakeAvailableOrder` 附近）

```csharp
        /// <summary>(Fill fairness) O(1) check whether an order is still in the available-order
        /// backlog (used to release a split parent exactly once). </summary>
        public bool IsOrderAvailable(Order order) { lock (_syncRoot) { return _availableOrders.Contains(order); } }
```

### 5.3 釋出邏輯（`SplitM2eICManager.cs` DispatchLoop 尾端,`foreach (var parent in result.SplitParents)`)

現況:
```csharp
            foreach (var parent in result.SplitParents)
            {
                if (double.IsPositiveInfinity(parent.TimeStampSubmit))
                    parent.TimeStampSubmit = Instance.Controller.CurrentTime;
                if (parent.IsFullyClaimed)
                {
                    _pendingOrders.Remove(parent);
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(parent);
                }
            }
```

改為:
```csharp
            foreach (var parent in result.SplitParents)
            {
                if (double.IsPositiveInfinity(parent.TimeStampSubmit))
                    parent.TimeStampSubmit = Instance.Controller.CurrentTime;
                // (Fill fairness) On the parent's FIRST split, free its Fill backlog slot so a
                // fresh order is injected, while keeping it in _pendingOrders for residual service.
                // Guarded to fire exactly once (skipped when off => bit-identical to current).
                if (_icConfig != null && _icConfig.ReleaseParentOnFirstSplit
                    && parent.IsSplitParent
                    && (Instance.ItemManager as ItemManager).IsOrderAvailable(parent))
                {
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(parent);
                }
                if (parent.IsFullyClaimed)
                {
                    _pendingOrders.Remove(parent);
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(parent);
                }
            }
```

`_icConfig`(SplitM2eICConfiguration)已是 manager 既有欄位(`SplitM2eICManager.cs:30,48`)。

## 6. 驗收

1. **Build**:x64 Release 無誤。
2. **Zero-drift(flag off = 現況)**:`ReleaseParentOnFirstSplit` 未設(預設 false),Fill 模式 tier 跑 `small_o100_mu100_4h_inv50.xsett`,kpi_report.csv 與改動前逐位一致(新 `if` 區塊整段跳過)。
3. **效果(flag on)**:同場景 flag on vs flag off——flag on 應注入更多新鮮單(orders/items 吞吐上升);記錄 `_pendingOrders` 是否膨脹(MILP solve 時間、pending 大小)。
4. **主結果**:Fill 模式、seed 0/1,**tier(flag on)vs M1G** 比 items/orders 吞吐 + pile-on + 能耗;目標=tier 吞吐勝 M1G 且維持能耗/pile-on 優勢。
5. **交叉檢查**:與 Fixed 模式既有結論一致(tier 勝);pending 集不失控導致 MILP 不可解。

## 7. 風險

- **R1 pending 膨脹**:tier 吸單變快,`_pendingOrders` 殘餘累積 → MILP 變大變慢。先不設上限;驗收步驟 3 監看 solve 時間與 pending 大小,若失控再加 cap(另議)。
- **R2 殘餘永不完成**:某 SKU 缺貨使母單殘餘永遠無法拆完 → 母單卡 `_pendingOrders`。此為**現況既有行為**(與本修法無關),不在本 spec 範圍。
- **R3 config gating 失誤**:務必確認 flag off 時新 `if` 整段跳過(zero-drift 步驟 2 把關)。

## 8. 可回退

- 全程 config-gated,`ReleaseParentOnFirstSplit=false` = 逐位等同現況。
- 徹底移除 = revert 三處小改(config 欄位、ItemManager accessor、release 區塊)。
- 建議從 `tier-current`(純現狀 tier,無 RL)開新分支做,與 RL 分支隔離。
