# Design: M1G / HADGS Station-Starve-Aware Cost (delay-time)

> Date: 2026-06-06
> Status: design approved, pending implementation plan
> Scope: RAWSimO.Core OrderBatching managers `M1GManager` and `HADGSManager`
> Goal: bias the OA+PS+TA assignment so output stations are kept from going idle
> (starving), by turning the existing distance cost into a travel-time cost and
> adding a starvation **delay-time** penalty.

---

## 1. Motivation

In the current M1G / HADGS managers, the order→station / pod→station / bot→pod
assignment is driven by **distance** cost. Distance alone is blind to whether a
station is about to run out of pod-work (starve). A station can have its order
capacity full (`us = 0`) yet still go idle because pods do not physically arrive
in time — starvation is a *time / pod-arrival* phenomenon, not an order-capacity
one.

This design makes the assignment **starve-aware**: when a station is close to
going idle, the next pod (and the bot to fetch it) should be steered toward it,
even at some distance cost, so the station's pipeline never empties.

It builds directly on the starvation instrumentation added earlier this cycle:
`OutputStation.StatStarvationTimeSec` (`OutputStation.cs:603`, only counted after
the station's first completed output) and the forward projection
`SlowStartController.ComputeStationWorkProjection(...).FirstStarveSec`
(exposed via `OutputStation.GetInfoStationEST()` at `OutputStation.cs:828`).

---

## 2. Current behaviour (verified)

### 2.1 M1G objective (`M1GManager.cs:565-580`)
```
minimize  w1 · ( Σ xps · EstimatePodStationDistance(p,s)        // pod→station, M1GManager.cs:154
                + Σ yrp · EstimateBotPodDistance(r,p) )         // bot→pod,    M1GManager.cs:117
        + w2 · Σ yos                                            // w2 = -40, order-served reward
        + w3 · Σ us                                             // w3 = 1000, unused order-capacity penalty
```
`xps`(pod,station) and `yrp`(robot,pod) are **separate** terms, linked by
constraint `shi8` (`M1GManager.cs:613`): `Σ xps[p] ≤ Σ yrp[p]` (a pod assigned
to a station must have a robot assigned to it). `yrp` variables exist only for
**available** robots `R` (`M1GManager.cs:488-509`).

### 2.2 No-feasible-bot behaviour (both managers — verified)
Both managers **defer** when no bot is available; neither creates a task it
cannot staff:
- **HADGS**: `if (Ra.Count == 0) continue;` (`HADGSManager.cs:585-586`). Pod and
  bot are claimed atomically (`HADGSManager.cs:622-623`).
- **M1G**: with `R` empty, `shi8` forces `xps = 0` for unused pods → no new
  pod→station assignment; orders stay pending.

**Implication:** the starve-aware cost has no effect when bots are *zero* (there
is nothing to allocate). It matters in the realistic regime — **bots scarce but
non-zero, multiple stations competing** — where it decides *which* station the
limited pods+bots go to first. The signal influences *competition ordering*, not
task creation out of nothing.

---

## 3. Design

### 3.1 Distance → travel time (shared)
Reinterpret the two existing estimators in **time** units (ideal, no-conflict
ETA), not distance:
- `time(pod→station)` — carrying ETA, replaces `EstimatePodStationDistance(p,s)`
- `time(bot→pod)` — ETA, replaces `EstimateBotPodDistance(r,p)`

Use the existing ideal-kinematic ETA path: `EstimateIdealKinematicEta`
(`WHCAnStarPathManager.cs:257`) with fallback to
`SlowStartController.ComputeIdealEta` (`SlowStartController.cs:84`), matching the
fallback chain already used by the release schedulers.

### 3.2 Delay-time penalty (starve-aware core)
For a candidate (bot r, pod p, station s):
```
TA_cost(r,p,s) = time(bot→pod) + time(pod→station)
EST(s)         = FirstStarveSec(s)            // seconds until station s goes idle
delay(r,p,s)   = TA_cost − EST(s)
penalty        = (delay ≥ 0) ? delay : FixedParam
```
- `delay ≥ 0`: the pod cannot reach the station before it starves even under
  ideal travel → unavoidable starvation gap → penalty = `delay` (proportional).
- `delay < 0`: the pod can ideally arrive in time (ideal lower bound met) →
  penalty = `FixedParam`, a **small positive constant** (keeps an early pod's
  cost non-negative so the solver cannot exploit a negative cost; it is the
  "ideal-lower-bound" floor).

The total assignment cost = **travel-time base cost + delay penalty** (both
present; they serve different purposes — efficiency vs. starvation avoidance).

### 3.3 Injection points (separately)

**M1G** — coefficients are constants evaluated before solving, so the penalty is
**precomputed**, preserving the linear structure (no new variables, `w1/w2/w3`
untouched):
- `xps` coefficient → `time(pod→station)`; `yrp` coefficient → `time(bot→pod)`.
- Per (p,s), compute a representative `TA_cost = time(pod→station) +
  min over available bots of time(bot→pod)`, then `delay = TA_cost − EST(s)` and
  the piecewise penalty, and **add it into the `xps` coefficient** as a constant.
- A small helper computes `EST(s)` once per epoch for `Cs.Keys` and caches the
  representative bot→pod time per pod.

**HADGS** — *(corrected after code inspection)* HADGS is a greedy heuristic, not
a MILP with distance coefficients. Its pod scoring is **demand-based**
(`HADGSManager.cs:428-439`: Demand / Completeable / WorkAmount); pod→station
distance does **not** enter pod selection, and distance is used only to pick the
nearest bot for an already-chosen pod (`HADGSManager.cs:599`). So the M1G
coefficient-swap does not map onto HADGS. Instead, inject starvation-awareness at
the **station processing order** (approach 甲):
- The per-station POA/PPS loop in `HeuristicsPOAandPPS` (`HADGSManager.cs:509-511`)
  currently iterates `Instance.OutputStations` in natural order; whichever station
  is processed first claims the limited available bots first (`HADGSManager.cs:585`
  `if (Ra.Count == 0) continue;`).
- When `StarveAwareCostEnabled` is true, order the station loop by
  `StarveAwareCost.Est(station, now)` **ascending** (closest-to-idle first) so the
  most-urgent station grabs bots/pods first. EST is recomputed each outer pass, so
  priority updates as assignments register inbound pods.
- Baseline preservation: with the flag off, the sort key is a constant `0.0`;
  `OrderBy` is a stable sort, so the original `OutputStations` order is preserved
  exactly (zero behavioural change).
- This reuses the shared `StarveAwareCostEnabled` flag and the `StarveAwareCost.Est`
  helper. The delay-penalty / travel-time cost model (§3.2) applies to M1G only;
  HADGS uses EST ordering, which is the directly actionable lever given its
  demand-based pod scoring.

### 3.4 Gating (ablation)
New settings (default OFF → behaviour identical to current baseline):
- `StarveAwareCostEnabled` (bool, default `false`)
- `StarveAwareFixedParam` (double) — the `FixedParam` floor
- travel-time conversion reuses existing ETA configuration

When disabled, the managers use the original distance cost unchanged.

### 3.5 Isolation
- New per-station urgency / EST computed in a small single-purpose helper; reads
  `SlowStartController.ComputeStationWorkProjection` (no mutation).
- Does not modify `SlowStartController`, the WHCA* planner, or the reservation
  table. Estimator stays side-effect-free.

---

## 4. Validation

Primary KPI: `StatStationStarvationTimeSec` (per station) /
`StatOverallStationStarvationTimeSec` (fleet, `InstanceStatistics.cs:201`),
compared ON vs OFF under matched seeds.

Success criteria:
- Station starvation time ↓.
- Throughput / station occupancy not degraded (ideally ↑).

Standard test cases (per project convention): large bot=45 / small bot=20,
HADGS + WHCA*n-P.

---

## 5. Risks / open items
- **M1G representative bot→pod approximation**: using the min available bot→pod
  time per pod is an approximation of the true (r,p,s) TA_cost; the chosen robot
  in the optimal solution may differ. Acceptable because the penalty only needs
  to rank starvation urgency, not be exact.
- **Double-counting concern**: `TA_cost` appears both in the base travel cost and
  inside `delay`. This is intentional (two objectives) but the relative scale of
  `FixedParam` vs. travel time must be tuned so the penalty does not dominate.
- **Small layouts**: when `FirstStarveSec` is frequently ~0 (chronic
  starvation), nearly every candidate hits `delay ≥ 0`; the penalty then behaves
  like an additive travel-time term — verify it still discriminates between
  stations.
- `FixedParam` and the travel-time scale are tuning parameters; defaults to be
  set during implementation and swept in validation.

---

## 6. Out of scope
- Restructuring the M1G objective into joint (r,p,s) variables (the
  `congestion-aware-cost` Phase D track). This design deliberately keeps the
  separated xps/yrp structure.
- Changing `w1/w2/w3`, the order-served reward, or the unused-capacity penalty.
- Input-station (replenishment) starvation — output stations only.
