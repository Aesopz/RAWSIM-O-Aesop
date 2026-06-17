# Slow-Start Phase 1a Smoke Test — Result Report

**Date**: 2026-05-20
**Spec**: `docs/superpowers/specs/2026-05-20-slow-start-pod-release-design.md` (rev2)
**Configuration**: small layout (1-6-12-20-0.89-large.xlayo) · N=20 · sequ OB · WHCA*n-P · 7200s · single seed=0

---

## TL;DR

Slow-start hold actually triggered for **only ~29 s total** across all 20 bots over 7200 s — fired for a single bot. Despite this tiny activation footprint, the run shows **modest but consistent improvement** on the metrics that matter for the user's hypothesis:

- **Throughput** preserved (slightly improved): **+2.5 %**
- **Station-queue premature wait time** dropped: **−5.6 %**
- **Station-queue premature wait energy** dropped: **−5.6 %**
- **Energy per order** dropped: **−2.5 %**
- **Path-conflict (CBS) wait** *rose* +8 % — flagged as reviewer-anticipated hold-cell impact

Phase 1a does NOT prove the hypothesis at significance — single seed, slow-start barely fires. But the *direction* of every primary KPI matches the spec hypothesis, so it earns Phase 1b (large/N=60 multi-seed).

---

## KPI table

| Metric | Baseline | Treatment | Δ | Spec gate |
|---|---:|---:|---:|---|
| Orders completed | 1205 | 1235 | **+30 (+2.5 %)** | ≥ baseline · ✅ |
| Orders / hour | 602.5 | 617.5 | +15 | ✅ |
| **StatQueueingAtStationTimeSec** | 11506.2 | 10865.9 | **−640 s (−5.6 %)** | primary success · ✅ |
| **StatEQueueingAtStationKJ** | 1035.6 | 977.9 | **−57.6 kJ (−5.6 %)** | primary success · ✅ |
| Energy total + support (kJ) | 23155.6 | 23143.7 | −11.9 (−0.05 %) | observation · ~ |
| Energy per order (kJ/order) | 19.22 | 18.74 | **−2.5 %** | observation · ✅ |
| L5 wait time total (s) | 2795.7 | 3021.2 | **+225 (+8 %)** | constraint · ⚠️ |
| L5 wait energy (kJ) | 251.6 | 271.9 | +20.3 (+8 %) | constraint · ⚠️ |
| Turn count | 10287 | 10155 | −132 (−1.3 %) | observation · ~ |
| Distance (m) | 117293 | 117163 | −130 | ~ |
| Output station arrivals | 622 | 599 | −23 | fewer wasted trips · ✅ |
| Trip count | 3122 | 3073 | −49 | ~ |

## Strategy behavior

| Metric | Treatment |
|---|---:|
| Total time bots spent in `BotStateType.SlowStartHold` | **29.24 s** (single bot) |
| Bots with non-zero SlowStartHold time | 1 / 20 |
| Hypothesized cause of low activation | At N=20 small layout, station is saturated by sequential dispatch → T_starve ≈ 0 → most decisions yield immediate release (delay=0) |

## Interpretation

1. **Throughput +2.5 % at near-zero hold time is surprising.** Possible explanations:
   - Single-seed noise (most likely; need Phase 1b multi-seed)
   - The single bot's 29 s of hold happened to break a congestion cascade
   - Side effect of having `BotSlowStartHold` in state queue altering tick scheduling

2. **StatQueueingAtStationTimeSec drop is the cleanest signal.** Even with minimal slow-start firing, station-queue premature wait dropped 5.6 %. This is a non-zero effect, but small enough that we cannot rule out seed variance.

3. **Path-conflict wait rose 8 %** — this matches reviewer's concern #5: holding at pod cell may block other bots' paths. At N=20 the impact is small in absolute terms (225 s extra wait across 20 bots × 7200 s); Phase 1b will reveal whether this scales worse than benefits.

4. **Per-order energy improved 2.5 %** despite total energy roughly flat — because more orders were processed.

## Diagnostic gaps (per spec rev2 §4.3)

These counters were declared in spec/code but NOT yet wired into statistics.txt output (Phase 1 deferral):
- `StatSlowStartHoldTimeSec` (per-bot dedicated, separate from BotStateType.SlowStartHold)
- `StatSlowStartDecisionCount`
- `StatSlowStartImmediateReleaseCount`
- `StatSlowStartSearchFailures`
- `StatSlowStartExpectedArrivalMissingCount`
- `StatNeighborWaitImpactSec` (hold-cell impact proxy)

Phase 1b must add these to `PrintStatistics()` / `kpi_report.csv` before re-running.

## Verdict on Phase 1a goal

> Goal: "確保 station 不會餓死的條件下，透過將 queue waiting 的冗餘時間分散到交通狀況上，在不影響吞吐量的情況下，疏散交通達到更低的 energy consumption，消除壅塞帶來 stop and go"

| Sub-goal | Status |
|---|---|
| Station 不餓死 | ✅ Throughput preserved (+2.5 %), output_station_arrivals 維持 |
| 不影響吞吐量 | ✅ +2.5 % |
| 疏散交通 (queue 端) | ✅ StatQueueingAtStationTimeSec −5.6 % |
| 更低 energy consumption (per order) | ✅ kJ/order −2.5 % |
| 消除 stop-and-go | ⚠️ path-conflict wait +8 %（hold-cell impact），需 Phase 1b 驗證是否在 large/N=60 下被 station-queue 節省壓過 |

**Net**: smoke test passes — direction-of-effect correct on 4/5 primary KPIs, 1 concerning side effect (path wait up). Hypothesis still alive. **Proceed to Phase 1b**.

## Next steps

1. Wire dedicated slow-start counters into `kpi_report.csv` so Phase 1b can see decision count / immediate release ratio / search failures directly
2. Add `StatNeighborWaitImpactSec` instrumentation to quantify hold-cell impact precisely
3. Phase 1b: large layout · N=60 · 3-5 seeds · paired baseline vs treatment
4. Investigate why slow-start barely fires at N=20: is T_starve computation correct? Add log of (T_starve, ETA, delay) per decision for next run

## Artifacts

- Baseline: `analysis/slowstart_2026-05-20/baseline/1-6-12-20-0.89-large-small_sett-sequ-whcan-p-0/`
- Treatment: `analysis/slowstart_2026-05-20/treatment/1-6-12-20-0.89-large-small_sett_slowstart-sequ-whcan-p-0/`
- Spec: `docs/superpowers/specs/2026-05-20-slow-start-pod-release-design.md`
- Plan: `docs/superpowers/plans/2026-05-20-slow-start-pod-release-impl.md`
