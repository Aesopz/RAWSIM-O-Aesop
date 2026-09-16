# 實例檔命名規範

2026-09-13 · 適用於 `Material/Instances/` 下的 `.xlayo` / `.xsett` / `.xconf` / `.xinst`

---

## 為什麼需要這份規範

模擬輸出目錄的名字由引擎組成：

```
<NameLayout> − <xsett 的 Name> − <xconf 的 Name> − <seed>
```

三段目前**全部失準**，導致目錄名沒有辨識力：

```
out/ch4_P3_s0/1-2-2-10-0.84-small_o100_mu100_4h_inv70-m1g-0/
              └ 看不出機隊     └ 謊報 4 小時（實際 2 小時）   └ 這其實是 M4G
```

實測的錯誤清單：

| 類型 | 症狀 | 數量 |
|---|---|---|
| `.xconf` | `m4g` / `m4g_ns` / `m4g_ns_s8` / `m4g_pb*` / `m4g_n*` 的內部 `<Name>` **全都是 `m1g`** | M4G 家族全部 |
| `.xlayo` | 6 / 8 / 10 / 12 bot 的 `<NameLayout>` **全是 `1-2-2-10-0.84`** | 四個 layout 撞名 |
| `.xsett` | `small_o100_mu100_2h_inv70` 的 `<Name>` 是 `..._4h_inv70`；`gate_2h` 的是 `gate_8h` | 17 個檔案 |

後果：所有分析腳本都必須用 `glob("<自訂前綴>_s<seed>/*/statistics.txt")` 的萬用字元
繞過引擎產生的目錄名，而那個前綴是人工在跑批腳本裡取的，與檔案內容沒有任何強制關聯。

---

## 規範

### 通則

> **檔名與內部名稱必須逐字相同。** 檔名是 `X.xsett`，內部 `<Name>` 就必須是 `X`。
> 這是唯一一條不可違反的規則，其餘格式細節都是為了讓這條規則有意義。

### `.xlayo`

```
<scale>_<bots>b_<pick>p_<repl>r[.<variant>]
```

| 欄位 | 意義 | 例 |
|---|---|---|
| `scale` | `small` / `large` | |
| `bots` | `BotCount` | `6b` |
| `pick` | 揀貨站總數（W+E+N+S） | `2p` |
| `repl` | 補貨站總數 | `2r` |
| `variant` | 非標準變體才加 | `.cap500` |

```
small_6b_2p_2r.xlayo       small_8b_2p_2r.xlayo
small_10b_2p_2r.xlayo      small_12b_2p_2r.xlayo
large_45b_12p_6r.xlayo
```

`<NameLayout>` 必須與檔名一致。

⚠️ 現行的 `1-2-2-10-0.84` 是產生器留下的編碼（層數-?-?-?-PodAmount），
既不可讀也不唯一，一律替換。

### `.xsett`

```
<scale>_<mode><orders>_<sku>_<hours>h_inv<pct>
```

| 欄位 | 意義 | 例 |
|---|---|---|
| `scale` | `small` / `large` | |
| `mode` | `fill`（滾動積壓）/ `fixed`（固定集合） | `fill` |
| `orders` | `OrderCount`（Fill 模式的池深） | `100` |
| `sku` | SKU 產生器 | `mu100` |
| `hours` | `SimulationDuration` 換算小時 | `2h` |
| `pct` | `InitialInventory` 百分比 | `inv70` |

```
small_fill100_mu100_2h_inv70.xsett      ← 小規模正典
large_fill200_mu1000_2h_inv70.xsett     ← 大規模正典（2026-09-15 更正：原寫 fill100_mu100，實際為 OrderCount 200、Mu-1000）
```

> 2026-09-15 起，正典範本統一放在 `Material/Instances/Canon/`（見該資料夾 README），
> 新實驗從範本複製到 `Material/Instances/Experiments/<日期>_<slug>/` 再改動。

`<Name>` 必須與檔名一致。

### `.xconf`

```
<model>[_<variant>]
```

```
m1g            M1G 基準
m4g            M4G 正典（拆單）
m4g_ns         M4G-NS（不拆單，訂單口徑定價）
m4g_ns_s8      M4G-NS 承載 M1G 目標式
hgs_m5         HGS-M5 貪婪
sequ_whcan     循序基準
m4g_pb50       正典 + 包裝站容量 50        實驗臂
m4g_n2         正典 + 份數上限 2           實驗臂
```

`<Name>` 必須與檔名一致。

---

## 正典值（全部固定，不再有變體）

```
模擬時長     7200 秒（2 小時）      所有比較實驗一律使用
起始庫存     70%
訂單模式     Fill；小規模 OrderCount 100、大規模 OrderCount 200
SKU          小規模 Mu-100.xgenc（100 種）、大規模 Mu-1000.xgenc（1000 種）
補貨         (s, S) = (0.65, 0.85)，庫存位置驅動

小規模       6/10 bots · 2 揀貨站 · 2 補貨站 · 站台容量 6 · 100 pods / 120 cells · cap 100
大規模       45 bots · 12 揀貨站 · 6 補貨站 · 站台容量 6 · 1203 pods / 1352 cells · cap 500
```

🚨 **包裝站容量與拆單份數上限不在正典內。**
`PackingStationCount` / `PackingBufferCapacity` / `MaxPartsPerOrder` 預設皆為 0，
正典 xconf 一個都不寫入，只有實驗臂才開啟。

---

## 跑模擬前的檢查清單

1. **確認 layout**：`BotCount` 與揀貨站數是不是你要的規模？
2. **確認 xsett**：`SimulationDuration` 是 7200 嗎？`InitialInventory` 是 0.7 嗎？
3. **確認 xconf**：是正典還是實驗臂？實驗臂的旗標值對嗎？
4. **確認輸出目錄名**：改名完成後，目錄名應該能直接讀出三段設定。
   若讀不出來，代表某個檔案的內部名稱又不同步了。

⚠️ **不要憑記憶引用設定值。** 正典會隨掃描更新，引用前先讀檔案。

---

## 改名的執行順序

1. 等所有跑批結束（改 `<Name>` 會讓之後的場次落到不同目錄，同批資料被切開）
2. 先改 `.xconf` 的 `<Name>`（影響最大、修正最明顯）
3. 再改 `.xlayo` 的 `<NameLayout>` 與檔名
4. 最後改 `.xsett` 的 `<Name>` 與檔名
5. 同步更新 `scripts/*.cmd` 裡的路徑
6. 跑一場煙霧測試，確認輸出目錄名可讀
7. 舊的輸出目錄**不重新命名**——它們屬於舊命名時代，保留原樣即可

⚠️ 改名不會改變模擬行為（`<Name>` 只進統計目錄名），但**會改變輸出路徑**，
所以既有的彙總腳本若寫死了目錄名就要同步改。目前所有腳本都用萬用字元，不受影響。
