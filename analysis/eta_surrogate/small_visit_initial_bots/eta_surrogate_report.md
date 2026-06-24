# ETA Surrogate Training Report

Sample CSV: `analysis\eta_surrogate\small_visit_initial_bots\eta_samples.csv`

| kind | features | unique leg groups | test n | MAE | RMSE | R2 | P90 abs | under rate | P90 under margin | max under |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| bot_to_pod | cheap | 1500 | 600 | 0.477 | 0.684 | 0.974 | 1.274 | 0.470 | 1.243 | 1.679 |
| bot_to_pod | path | 1500 | 600 | 0.226 | 0.293 | 0.995 | 0.451 | 0.510 | 0.435 | 1.350 |
| pod_to_station_queue | cheap | 200 | 600 | 3.111 | 4.158 | 0.520 | 5.683 | 0.475 | 6.881 | 7.041 |
| pod_to_station_queue | path | 200 | 600 | 0.194 | 0.239 | 0.998 | 0.326 | 0.575 | 0.324 | 0.570 |

Interpretation:
- `under_rate` is the share of test samples where the model predicted too low.
- `p90_under_margin` is a candidate safety buffer to add when underestimation is risky.
- Train/test split is grouped by leg identity, so repeated visit rows for the same physical leg do not leak into both sets.
- Use the cheap model only if its P90 absolute error and underestimation margin are acceptable for station-starvation trigger timing.
