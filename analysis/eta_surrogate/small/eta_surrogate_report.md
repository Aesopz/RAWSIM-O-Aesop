# ETA Surrogate Training Report

Sample CSV: `analysis\eta_surrogate\small\eta_samples.csv`

| kind | features | test n | MAE | RMSE | R2 | P90 abs | under rate | P90 under margin | max under |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| bot_to_pod | cheap | 28080 | 0.499 | 0.745 | 0.974 | 1.230 | 0.478 | 1.270 | 5.070 |
| bot_to_pod | path | 28080 | 0.229 | 0.279 | 0.996 | 0.451 | 0.511 | 0.432 | 1.091 |
| pod_to_station_queue | cheap | 40 | 3.435 | 4.381 | 0.593 | 7.160 | 0.400 | 9.092 | 12.485 |
| pod_to_station_queue | path | 40 | 0.641 | 0.964 | 0.980 | 1.157 | 0.575 | 2.424 | 3.088 |

Interpretation:
- `under_rate` is the share of test samples where the model predicted too low.
- `p90_under_margin` is a candidate safety buffer to add when underestimation is risky.
- Use the cheap model only if its P90 absolute error and underestimation margin are acceptable for station-starvation trigger timing.
