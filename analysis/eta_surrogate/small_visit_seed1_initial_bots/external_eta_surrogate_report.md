# External ETA Surrogate Evaluation

Sample CSV: `analysis\eta_surrogate\small_visit_seed1_initial_bots\eta_samples.csv`

| kind | n | buffer | MAE | RMSE | R2 | P90 abs | under rate | max under | buffered under rate | buffered max under |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| bot_to_pod | 3000 | 2.000 | 0.198 | 0.255 | 0.996 | 0.414 | 0.529 | 1.350 | 0.000 | 0.000 |
| pod_to_station_queue | 3000 | 1.000 | 0.175 | 0.215 | 0.999 | 0.328 | 0.535 | 0.570 | 0.000 | 0.000 |

Buffered underestimation is computed as `actual - (prediction + buffer)` when positive.
