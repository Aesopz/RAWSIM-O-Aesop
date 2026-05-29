# Congestion-Aware Slow-Start Roadmap

Date: 2026-05-21
Status: Research roadmap after current slow-start implementation audit

## Current Implementation Baseline

The current `SlowStartController` implements a station-slack rule:

```text
delay = max(0, T_starve - ETA)
```

This is a useful Phase 1 baseline, but it does not yet implement the full research idea. It only asks whether the output station has enough busy-time slack to let the bot wait at the pod cell. It does not explicitly ask whether departing now is worse than departing later under local traffic conditions.

The current runtime release behavior is:

1. A bot enters `BotSlowStartHold` after pod pickup and before pod-to-station travel.
2. `ComputeHold()` estimates pod-to-station ETA and station starvation slack.
3. If `delay > 0`, the bot holds at the pod cell and wakes every 1 second.
4. The bot releases at the hard deadline, or earlier if the current reservation-aware ETA fits inside the remaining station-busy window plus a 1-second tolerance.

Current `T_starve` is intentionally conservative and only counts station work already physically present: the station's current busy window, pending item requests, and other bots that are actually queueing at the station. It excludes in-flight pods and other slow-start holders.

## Gap Against The Research Objective

The user objective is broader:

> Convert low-value station queue waiting into higher-value upstream waiting, and choose the release timing from warehouse state so that the bot avoids stop-and-go / congestion energy without starving the station.

The current implementation only satisfies the station-safety half. It does not yet provide a strong answer to "best release timing" because:

1. It has no explicit congestion score at the pod cell, route corridor, or station approach.
2. It does not compare multiple possible departure times.
3. It does not estimate uncertainty in ETA or station starvation risk.
4. It uses a fixed 1-second re-probe and a simple release condition, not an optimization over slack budget.
5. The smoke run barely activated the hold, so observed KPI movement is not enough evidence for the final strategy.

## Research Requirements

A defensible strategy needs to satisfy these requirements.

### R1. Station Safety

The release policy must bound the risk that the station becomes idle because a bot waited too long.

Evidence needed:

- `OutputStation.StatIdleTime` should not increase materially versus paired baseline.
- A new starvation miss diagnostic should count cases where the station is idle while a held bot is still upstream or arrives late.
- Predicted-vs-actual arrival error must be logged for slow-start decisions.

Suggested gate:

```text
orders_completed >= baseline * 0.99
station_idle_time_delta <= small tolerance
starvation_miss_count close to 0
```

### R2. Wait Conversion

The policy should show that station-side queue waiting is being converted into intentional upstream hold, not merely hidden or double-counted.

Evidence needed:

- `StatQueueingAtStationTimeSec` decreases.
- `StatSlowStartHoldTimeSec` increases.
- Existing congestion wait metrics do not absorb the hold time.

Suggested conversion metric:

```text
conversion_ratio =
  treatment.StatSlowStartHoldTimeSec /
  max(1, baseline.StatQueueingAtStationTimeSec - treatment.StatQueueingAtStationTimeSec)
```

A ratio near 1 means station queue wait is mostly being moved upstream. A ratio far above 1 means over-holding. A ratio near 0 means the strategy barely activates.

### R3. Congestion Benefit

The policy must reduce stop-and-go or path-conflict cost, not just move waiting from one label to another.

Evidence needed:

- Path-conflict wait does not increase.
- Turn count / acceleration proxy does not increase.
- Energy per order improves or stays flat while throughput is preserved.
- Neighbor wait around pod-cell hold locations is measured.

Suggested gate:

```text
path_conflict_wait_delta <= 0
neighbor_wait_impact <= 0.3 * station_queue_wait_saved
energy_per_order_delta <= 0
```

### R4. Release Timing Quality

The release policy should choose better departure timing than both immediate departure and fixed hold.

Evidence needed:

- Per-decision trace of candidate departure times.
- For each candidate, log predicted route ETA, congestion score, station slack, and chosen reason.
- Ablation against simple rules:
  - immediate release
  - current station-slack rule
  - fixed hold, e.g. 5s/10s
  - congestion-aware release

## Candidate Release Policies

### Policy A: Current Station-Slack Baseline

Release when:

```text
ETA_now <= remaining_T_starve + tolerance
```

Role: baseline safety controller. It should remain as the fallback policy.

Limitation: if ETA_now is feasible but congested, the bot still departs immediately.

### Policy B: Reservation ETA Improvement

At each 1-second probe, estimate:

```text
ETA_now
ETA_later(k) for k in {1, 2, 3, ..., min(slack_budget)}
```

Release if either:

```text
currentTime >= hard_deadline
```

or:

```text
ETA_now <= min_future_ETA + release_margin
```

and station safety remains satisfied:

```text
ETA_now <= remaining_T_starve - eta_safety_buffer
```

Interpretation: wait only when a short delay is expected to buy a better route.

### Policy C: Route-Corridor Congestion Score

Compute a local congestion score for the likely pod-to-station corridor:

```text
score = weighted sum of:
  reserved cell-time density along route corridor
  number of bots within radius R of corridor
  expected waits returned by SpaceTimeAStar
  station-approach queue occupancy
```

Release if:

```text
score_now <= threshold
```

or if waiting would violate station safety.

This is easier to implement than CNN and gives an interpretable benchmark.

### Policy D: Heatmap / CNN Surrogate

Use heatmap or local grid tensor around the bot / route / station to predict:

```text
E[travel_time]
E[path_wait_time]
E[energy]
P(arrival_late_for_station)
```

Then choose the release time that minimizes:

```text
objective(k) =
  predicted_energy(k)
  + lambda_wait * predicted_path_wait(k)
  + lambda_starve * P(station_starves | k)
```

subject to:

```text
P(station_starves | k) <= epsilon
```

This should be Phase 3, after Policy B/C produce decision traces and labels.

## Data To Log Next

Add a per-decision CSV before large experiments:

```text
time,bot_id,task_id,pod_id,station_id,
eta_now,t_starve,slack,chosen_delay,deadline,
probe_success,release_reason,
station_pending_items,station_busy_remaining,station_queue_work,
route_wait_predicted,route_eta_predicted,
actual_arrival_time,actual_eta,arrival_error,
station_idle_at_arrival,queue_wait_at_station,
hold_cell_neighbor_wait,hold_cell_neighbor_count
```

This log is more important than another aggregate KPI run. Without decision traces, a KPI delta cannot explain whether the release rule is actually making good choices.

## Experiment Plan

### E0. Instrumentation Validation

Layout: small
Fleet: 20
Duration: 7200s
Seeds: 1

Goal:

- Prove the decision CSV is populated.
- Prove slow-start counters in `statistics.txt` and aggregate KPI files agree.
- Confirm no hold time is counted as ordinary path-conflict wait.

Do not draw performance conclusions from E0.

### E1. Large Signal Check

Layout: large
Fleet: 60
Duration: 7200s
Seeds: 3-5 paired
Policies:

- baseline immediate release
- current station-slack slow-start

Goal:

- Confirm whether slow-start activates often enough in the real congestion regime.
- Determine whether pod-cell holding causes neighbor impact that outweighs station queue savings.
- Decide whether to keep pod-cell hold or move to staging-cell hold.

### E2. Rule-Based Congestion Release

Layout: large
Fleet: 45 and 60
Duration: 7200s
Seeds: 3-5 paired
Policies:

- immediate release
- station-slack rule
- reservation ETA improvement rule
- route-corridor congestion score rule

Goal:

- Test whether explicit congestion timing beats station-slack alone.
- Keep the path planner fixed.
- Use paired seeds and identical order streams.

### E3. Surrogate Feasibility

Use decision traces from E1/E2 as labels.

Targets:

- actual pod-to-station travel time
- actual path-conflict wait
- actual energy
- late-arrival / station-starvation risk

Models:

- simple regression on engineered congestion features
- heatmap/grid CNN only if engineered features leave meaningful residual signal

Gate before CNN:

```text
large-run decision count is high enough
travel-time / wait residual has nontrivial variance
engineered baseline is not already sufficient
```

## Immediate Next Implementation Step

Do not jump directly to CNN.

The next code step should be decision tracing for the current station-slack implementation. That gives the labels needed to answer:

1. How often is there actually slack to hold?
2. How often does a later release improve reservation-aware ETA?
3. How wrong is the ETA estimate?
4. Does holding at the pod cell hurt nearby traffic?
5. Did any hold cause station starvation?

After that trace exists, the next policy should be Policy B, because it is the smallest change that directly tests the core idea: use queue-time slack as a budget, but release only when congestion has improved or the safety deadline is near.

## 2026-05-21 Trace Smoke Evidence

Decision tracing now exists for the station-slack baseline and writes `slowstart_decisions.csv`. Two 600-second smoke runs were used only as instrumentation checks, not performance claims.

### SEQU / WHCAn-P Smoke

Run:

```text
analysis/slowstart_trace_smoke_2026-05-21/1-6-12-20-0.89-large-small_sett_slowstart_trace_smoke-sequ-whcan-p-0
```

Validation:

```text
decision_rows=52
positive_hold_rows=0
release_reasons=immediate_release:52
arrival_error_abs_mean=6.326458
arrival_error_abs_max=42.990000
queue_wait_mean=7.223333
queue_wait_max=40.240000
status=PASS
```

Audit:

```text
missed_conversion_rows=3
eta_underestimate_p90=5.980000
eta_underestimate_p95=15.860000
conservative_convertible_after_p90_buffer_rows=3
```

Interpretation: in the simpler SEQU case, slow-start did not activate, but there are still a few realized station-queue waits that could have been converted upstream with a modest safety buffer.

### HADGS / WHCAn-P Smoke

Run:

```text
analysis/slowstart_trace_smoke_2026-05-21_hadgs/1-6-12-20-0.89-large-small_sett_slowstart_trace_smoke-hadgs-whcan-p-0
```

Validation:

```text
decision_rows=35
positive_hold_rows=2
release_reasons=hard_deadline:2,immediate_release:33
arrival_error_abs_mean=24.462941
arrival_error_abs_max=113.160000
queue_wait_mean=29.665294
queue_wait_max=117.210000
status=PASS
```

Audit:

```text
missed_conversion_rows=14
eta_underestimate_p90=82.740000
eta_underestimate_p95=104.850000
conservative_convertible_after_p90_buffer_rows=4
```

Interpretation: HADGS has much larger station queue waits, but the current station-slack rule barely activates. The two positive holds both released by hard deadline and still arrived with large queue waits, so they did not starve the station. However, ETA underestimation is severe in the congested HADGS smoke, so any next release policy needs an explicit uncertainty buffer. A fixed small tolerance is not defensible.

## Next Policy Implication

The next implementation should not simply increase `T_starve` or force more holds. The evidence points to two separate quantities that must be tracked:

1. `queue_budget`: how much station-side queue wait is likely convertible into upstream waiting.
2. `eta_safety_buffer`: how much uncertainty must be reserved so the station does not become idle.

Policy B should therefore be tested in two layers:

```text
safe_budget = max(0, queue_budget - eta_safety_buffer)
release if hard_deadline is reached
release if ETA/congestion has improved enough
keep holding only while safe_budget remains positive
```

For the first rule-based experiment, estimate `eta_safety_buffer` from decision traces using at least p90 positive ETA underestimation, then compare against p95 in sensitivity analysis. CNN or heatmap models should wait until larger E1/E2 traces show that engineered congestion features cannot explain the residual travel-time error.

## 2026-05-21 Policy B Prototype Evidence

Implemented a switchable `SlowStartReleasePolicy`:

```text
StationSlack
ReservationEtaImprovement
```

`StationSlack` remains the default baseline. `ReservationEtaImprovement` adds a short-horizon future ETA probe during hold:

```text
for k in 1..SlowStartEtaImprovementLookaheadSec:
  estimate ETA if departing at currentTime + k
release now if current ETA is within SlowStartEtaImprovementReleaseMargin
of the best feasible future ETA
```

The policy does not relax station safety. For non-baseline policies, `SlowStartEtaSafetyBuffer` is subtracted from the initial station-slack hold budget:

```text
delay = max(0, T_starve - ETA - SlowStartEtaSafetyBuffer)
```

New trace columns:

```text
release_policy,eta_safety_buffer,
best_future_eta_at_release,best_future_delay_at_release,eta_improvement_at_release
```

Smoke setting:

```text
Material/Instances/CoreBenchmark/small_sett_slowstart_etaimprove_trace_smoke.xsett
```

Build command that avoids locked release/intermediate files:

```text
dotnet build RAWSimO.CLI/RAWSimO.CLI.csproj -c Release -p:Platform=x64 -p:OutputPath=bin/x64/TraceSmokePolicyB/ -p:BaseIntermediateOutputPath=obj/TraceSmokePolicyB/ --no-restore
```

SEQU smoke:

```text
analysis/slowstart_policyb_trace_smoke_2026-05-21/1-6-12-20-0.89-large-small_sett_slowstart_etaimprove_trace_smoke-sequ-whcan-p-0
decision_rows=52
positive_hold_rows=0
release_reasons=immediate_release:52
status=PASS
```

HADGS smoke:

```text
analysis/slowstart_policyb_trace_smoke_2026-05-21_hadgs/1-6-12-20-0.89-large-small_sett_slowstart_etaimprove_trace_smoke-hadgs-whcan-p-0
decision_rows=35
positive_hold_rows=2
release_reasons=hard_deadline:2,immediate_release:33
status=PASS
```

Interpretation: Policy B is now runnable and traceable, but this 600-second HADGS smoke did not trigger `eta_improvement_release`. The two positive holds still reached `hard_deadline`. Therefore this is an implementation-validity result, not an effectiveness result. A larger E2 run or a more active queue-budget policy is needed before judging whether future-ETA probing improves release timing.

## Autonomous Decisions Made

1. Treat the current station-slack slow-start as Phase 1 baseline, not the final congestion-aware strategy.
2. Prioritize decision tracing before CNN or heatmap modeling.
3. Keep station starvation as a hard constraint, not a weighted KPI.
4. Treat pod-cell neighbor impact as a first-class risk metric.
5. Use rule-based congestion policies before learned surrogates so the experiment has interpretable baselines.
6. Treat the 600-second smoke results as instrumentation evidence only, not KPI evidence.
7. Split future release logic into queue-budget estimation and ETA safety-buffer estimation instead of only changing `T_starve`.
8. Keep `StationSlack` as the default and add `ReservationEtaImprovement` as an explicit experiment variant instead of replacing the baseline.
9. Treat the current Policy B smoke as branch-validation only; it has not yet shown a release-timing win.
