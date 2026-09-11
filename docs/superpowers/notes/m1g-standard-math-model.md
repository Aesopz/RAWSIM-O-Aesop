# M1G 標準數學模型整理

本文整理目前 checkout 中 `RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs`
的 M1G MILP。符號以程式實作為準，重點是釐清 OA、PS、TA 在同一個 rolling
decision 中如何聯合決策。

## 1. 決策觸發與候選集合

每次決策前，M1G 先根據當前模擬狀態建立候選集合：

- 工作站集合 `S`：目前仍有可用 capacity slot 的 output stations。
- 訂單集合 `O`：目前 backlog 中、可由當前系統庫存滿足的 pending orders。
- pod 集合 `P`：包含已經指派到工作站且尚未釋放的 pods，以及目前可從儲位取用的 pods。
- robot 集合 `R`：所有可納入本次 TA 約束的 robots。
- `P^a`：本次可新指派的 available pods。
- `P^b`：已被指派且仍在路上、排隊、或工作站附近的 busy/inbound pods。
- `R^a`：本次可新指派的 available robots。
- `R^b`：已經綁定既有 pod task 的 busy robots。

若急單集合 `O^d` 的數量大於總可用 station capacity，M1G 會只把 `O^d` 放入 MILP。
急單由 `Timestay < 30 min` 類似邏輯產生；這是候選過濾，不是 objective 中的顯式
deadline reward。

## 2. 參數

集合與索引：

| 符號 | 意義 |
|---|---|
| `o ∈ O` | backlog 中的候選 order |
| `s ∈ S` | 本次有可用容量的 output station |
| `p ∈ P` | 本次模型中的 pod |
| `r ∈ R` | 本次模型中的 robot |
| `i ∈ I` | SKU |
| `O_i` | 需求包含 SKU `i` 的 orders |
| `P_i` | 庫存包含 SKU `i` 的 pods |

數值參數：

| 符號 | 意義 |
|---|---|
| `C_s` | station `s` 當前可用 order slots，程式中為 `Capacity - CapacityReserved - CapacityInUse` |
| `n_{i,o}` | order `o` 對 SKU `i` 的需求數量 |
| `a_{i,p}` | pod `p` 中 SKU `i` 的可用庫存數量 |
| `c^{PS}_{p,s}` | pod `p` 到 station `s` 的距離或成本 |
| `c^{RP}_{r,p}` | robot `r` 到 pod `p` 的距離或成本 |
| `w_1` | travel cost 權重，目前 hard-coded 為 `1` |
| `w_2` | coverable order reward 權重，目前 hard-coded 為 `-40` |
| `w_3` | unused station capacity penalty，目前 hard-coded 為 `1000` |

注意：distance cost 只對本次新取用的 unused pods 收費。已經 inbound 或 processing 的
pods 在本次模型裡通常被視為既有承諾狀態，不再重複計入新 travel cost。

## 3. 決策變數

| 程式變數 | 數學符號 | domain | 物理意義 |
|---|---|---|---|
| `xps_p_s` | `x_{p,s}` | binary | pod `p` 是否被分配到 station `s` |
| `yos_o_s` | `h_{o,s}` | binary | order `o` 是否可由 station `s` 的 pod 集合完整滿足 |
| `yaos_o_s` | `y_{o,s}` | binary | order `o` 是否真的被 assign 到 station `s` 的 slot |
| `yrp_r_p` | `b_{r,p}` | binary | robot `r` 是否被分配去搬 pod `p` |
| `us_s` | `u_s` | integer | station `s` 本次未使用的可用 slots |
| `dops_o_p_s` | `δ_{o,p,s}` | binary | new pod `p` 與 actual assigned order `o` 在 station `s` 的關係變數 |

這裡最重要的區分是：

```text
h_{o,s} = yos = 這張 order 可被 station s 的 pod 集合完整滿足
y_{o,s} = yaos = 這張 order 真的佔用 station s 的 capacity slot
```

M1G 的 objective reward 掛在 `h_{o,s}`，但最後真正呼叫 `AllocateOrder(o,s)` 的是
`y_{o,s}`。

## 4. 目標式

若本次有可用 robot (`R^a` 非空)，M1G objective 可寫為：

```math
\min
w_1
\left(
  \sum_{p \in P^a}\sum_{s \in S} c^{PS}_{p,s} x_{p,s}
  +
  \sum_{r \in R^a}\sum_{p \in P^a} c^{RP}_{r,p} b_{r,p}
\right)
+ w_2 \sum_{o \in O}\sum_{s \in S} h_{o,s}
+ w_3 \sum_{s \in S} u_s
```

若本次沒有可用 robot，程式會省略 robot-to-pod 成本項：

```math
\min
w_1
\sum_{p \in P^a}\sum_{s \in S} c^{PS}_{p,s} x_{p,s}
+ w_2 \sum_{o \in O}\sum_{s \in S} h_{o,s}
+ w_3 \sum_{s \in S} u_s
```

因為 `w_2 = -40`，所以 `\sum h_{o,s}` 是 reward：模型偏好讓選到的 pod 集合可以完整
覆蓋更多 backlog orders。這是 long-term pod coverage value，不等同於當期真正
assigned orders 的數量。

## 5. 限制式

### 5.1 每張 order 最多由一個 station 覆蓋

```math
\sum_{s \in S} h_{o,s} \le 1
\qquad \forall o \in O
```

物理意義：一張 order 在本次決策中最多被視為由一個 station 的 pod 集合滿足。這是原
M1G 不拆單的核心限制之一。

### 5.2 真正 assign 必須先可被滿足

```math
y_{o,s} \le h_{o,s}
\qquad \forall o \in O,\ s \in S
```

物理意義：只有當 order `o` 可由 station `s` 的 pod 集合完整滿足時，才允許把它真正
assign 到 station `s`。

### 5.3 station slot 守恆

```math
\sum_{o \in O} y_{o,s} = C_s - u_s
\qquad \forall s \in S
```

物理意義：station `s` 本次真正接收的 orders 數量，等於可用 slots 減掉未使用 slots。
由於 `u_s` 的懲罰 `w_3` 很大，只要有可行 order，模型通常會填滿 station 的可用 slot。

### 5.4 station-level SKU aggregate supply

```math
\sum_{o \in O_i} n_{i,o} h_{o,s}
\le
\sum_{p \in P_i} a_{i,p} x_{p,s}
\qquad \forall i \in I,\ s \in S
```

物理意義：對每個 SKU `i` 和 station `s`，所有被 `h_{o,s}=1` 覆蓋的 orders 對 SKU `i`
的需求總量，不得超過分配到 station `s` 的 pods 中 SKU `i` 的總庫存。

重要限制：這是 station-level aggregate inventory check，並沒有決定 `order o` 的
SKU `i` 具體由哪一個 pod `p` 供應。具體 `SKU -> pod -> order` 的 `Ziops` 分配是在
MILP 之後用 greedy post-processing 補出來。

### 5.5 每個 pod 最多分配到一個 station

```math
\sum_{s \in S} x_{p,s} \le 1
\qquad \forall p \in P
```

物理意義：同一個 pod 在同一決策中不能同時送往多個 stations。

### 5.6 既有 inbound / busy pods 固定到原 station

```math
x_{p,s} = 1
\qquad \forall s \in S,\ p \in P^b_s
```

其中 `P^b_s` 是目前已經被登記為 inbound 到 station `s` 的 pods。

物理意義：已經在路上、排隊、或正在為 station `s` 服務的 pod，在本次決策中繼續被視為
分配到該 station。

### 5.7 被分配到 station 的 pod 必須有 robot

```math
\sum_{s \in S} x_{p,s}
\le
\sum_{r \in R} b_{r,p}
\qquad \forall p \in P
```

物理意義：如果 pod `p` 被分配到某個 station，必須有 robot 負責搬運或已經綁定它。

### 5.8 每個 pod 最多由一台 robot 搬運

```math
\sum_{r \in R} b_{r,p} \le 1
\qquad \forall p \in P
```

物理意義：同一個 pod 不能同時由多台 robots 搬運。

### 5.9 每台 robot 最多搬一個 pod

```math
\sum_{p \in P} b_{r,p} \le 1
\qquad \forall r \in R
```

物理意義：同一台 robot 在同一決策中最多接受一個 pod task。

### 5.10 既有 busy robot-pod 配對固定

```math
b_{r,p} = 1
\qquad \forall (r,p) \in B
```

其中 `B` 是本次 decision snapshot 中已經存在的 robot-pod pairing。

物理意義：已經被分配且尚未完成的 robot-pod task 在模型中繼續保留。

### 5.11 order-pod-station 關係變數

```math
2\delta_{o,p,s}
\le
y_{o,s} + x_{p,s}
\qquad
\forall o \in O_i,\ p \in P_i \cap P^a,\ s \in S,\ i \in I
```

物理意義：只有當 order `o` 實際 assign 到 station `s`，且 new pod `p` 也被分配到
station `s` 時，`δ_{o,p,s}` 才可能等於 1。

注意：`δ_{o,p,s}` 不是 SKU 數量分配變數。它只表示 order-pod-station 之間可能存在供應
關係，不能保證 pod `p` 實際供應了多少 SKU 給 order `o`。

### 5.12 新分配 pod 必須關聯至少一張 actual assigned order

```math
x_{p,s}
\le
\sum_{o \in O} \delta_{o,p,s}
\qquad \forall p \in P^a,\ s \in S
```

物理意義：本次新叫來的 pod 不能完全無關於當期真正 assigned orders。這條限制避免
MILP 任意選一個新 pod 但沒有任何 assigned order 與它關聯。

## 6. 變數 domain

```math
x_{p,s}, h_{o,s}, y_{o,s}, b_{r,p}, \delta_{o,p,s} \in \{0,1\}
```

```math
u_s \in \mathbb{Z}_{\ge 0}
```

程式中的 `u_s` 上界是固定 integer range；語意上是 station `s` 未使用的可用 slots。

## 7. OA / PS / TA 的角色分解

在這個模型中：

- OA：由 `h_{o,s}` 和 `y_{o,s}` 表示。
  - `h_{o,s}` 是 coverage decision。
  - `y_{o,s}` 是 actual order-to-station assignment。
- PS：由 `x_{p,s}` 表示。
  - 決定 pod `p` 是否被送到 station `s`。
  - 已 inbound pods 會透過固定限制保留在原 station。
- TA：由 `b_{r,p}` 表示。
  - 決定 robot `r` 是否去搬 pod `p`。

三者透過 SKU aggregate supply、station capacity、pod-robot linking、以及 `δ_{o,p,s}` 關係式
被綁在同一個 MILP 中。

## 8. 對目前 M1G 的重要解讀

1. `\sum h_{o,s}` 是可滿足 order 的 coverage reward，不是實際完成訂單數。
2. 實際 assign 到 station 的 order 是 `y_{o,s}`，解後會被送進 `AllocateOrder(o,s)`。
3. deadline / urgency 主要透過候選集合 `O^d` 過濾進入模型，不是 objective 的顯式項。
4. SKU 庫存限制是 station-level aggregate，不是 exact SKU-to-pod-to-order flow。
5. 因為缺少 exact SKU attribution，M1G 解後仍需要 greedy `Ziops` 分配來把 selected pods 的 SKU
   實際扣到 selected orders 上。

## 9. 與後續 SplitM1G / SplitM1GExact 的關係

原 M1G 的不拆單限制主要體現在：

```math
\sum_{s \in S} h_{o,s} \le 1
```

以及 actual assignment `y_{o,s}` 只能讓一張 order 進入某個 station slot。

SplitM1G 放鬆的是 order-to-station 的粒度，改用 `q[o,i,s]` 表示 order 的 SKU 數量在 station
層級被分配多少；但它仍然沒有把 pod dimension 納入 `q`。

SplitM1GExact 進一步把 `q` 擴成 `q[i,o,p,s]`，目標是把 SKU-to-pod-to-order attribution
內生化，取代 M1G / SplitM1G 中解後 greedy `Ziops` 的落地步驟。
