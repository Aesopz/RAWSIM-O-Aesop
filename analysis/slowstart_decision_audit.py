#!/usr/bin/env python3
"""Audit slow-start decision traces for release-policy design signals."""

from __future__ import annotations

import argparse
import csv
import math
from collections import Counter, defaultdict
from pathlib import Path


def as_float(value: str) -> float | None:
    if value == "":
        return None
    try:
        result = float(value)
    except ValueError:
        return None
    if math.isnan(result) or math.isinf(result):
        return None
    return result


def percentile(values: list[float], pct: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    rank = (len(ordered) - 1) * pct
    lower = int(math.floor(rank))
    upper = int(math.ceil(rank))
    if lower == upper:
        return ordered[lower]
    weight = rank - lower
    return ordered[lower] * (1.0 - weight) + ordered[upper] * weight


def mean(values: list[float]) -> float:
    return sum(values) / len(values) if values else 0.0


def neighbor_bucket(count: int) -> str:
    if count <= 2:
        return "00-02"
    if count <= 5:
        return "03-05"
    if count <= 10:
        return "06-10"
    return "11+"


def audit(run_dir: Path, queue_wait_threshold: float, output_csv: Path | None) -> int:
    trace_path = run_dir / "slowstart_decisions.csv"
    if not trace_path.exists():
        raise FileNotFoundError(trace_path)

    with trace_path.open(newline="") as fh:
        rows = list(csv.DictReader(fh))

    eta_under_errors: list[float] = []
    queue_waits: list[float] = []
    positive_hold_rows = 0
    missed_conversion_rows = 0
    starvation_proxy_rows = 0
    release_reasons = Counter()
    bucket_stats: dict[str, dict[str, list[float] | int]] = defaultdict(
        lambda: {"rows": 0, "queue_waits": [], "eta_under": [], "chosen_delays": []}
    )
    audited_rows: list[dict[str, str]] = []

    for row in rows:
        eta_now = as_float(row.get("eta_now", ""))
        actual_eta = as_float(row.get("actual_eta", ""))
        chosen_delay = as_float(row.get("chosen_delay", "")) or 0.0
        queue_wait = as_float(row.get("queue_wait_at_station", "")) or 0.0
        station_idle = as_float(row.get("station_idle_at_arrival", "")) or 0.0
        neighbor_count = int(as_float(row.get("neighbor_count_at_decision", "")) or 0)
        release_reason = row.get("release_reason", "")

        release_reasons[release_reason] += 1
        if chosen_delay > 0.0:
            positive_hold_rows += 1
        if queue_wait > 0.0:
            queue_waits.append(queue_wait)

        eta_under = 0.0
        if eta_now is not None and actual_eta is not None:
            eta_under = max(0.0, actual_eta - eta_now)
            if eta_under > 0.0:
                eta_under_errors.append(eta_under)

        if chosen_delay <= 0.0 and queue_wait >= queue_wait_threshold and station_idle <= 0.0:
            missed_conversion_rows += 1
        if chosen_delay > 0.0 and station_idle > 0.0:
            starvation_proxy_rows += 1

        bucket = neighbor_bucket(neighbor_count)
        bucket_entry = bucket_stats[bucket]
        bucket_entry["rows"] = int(bucket_entry["rows"]) + 1
        bucket_entry["queue_waits"].append(queue_wait)  # type: ignore[union-attr]
        bucket_entry["eta_under"].append(eta_under)  # type: ignore[union-attr]
        bucket_entry["chosen_delays"].append(chosen_delay)  # type: ignore[union-attr]

        audited_rows.append(
            {
                "time": row.get("time", ""),
                "bot_id": row.get("bot_id", ""),
                "station_id": row.get("station_id", ""),
                "chosen_delay": f"{chosen_delay:.6f}",
                "queue_wait_at_station": f"{queue_wait:.6f}",
                "eta_underestimate": f"{eta_under:.6f}",
                "station_idle_at_arrival": f"{station_idle:.6f}",
                "neighbor_count_at_decision": str(neighbor_count),
                "release_reason": release_reason,
                "missed_conversion_opportunity": "1"
                if chosen_delay <= 0.0 and queue_wait >= queue_wait_threshold and station_idle <= 0.0
                else "0",
                "starvation_proxy": "1" if chosen_delay > 0.0 and station_idle > 0.0 else "0",
            }
        )

    safety_p50 = percentile(eta_under_errors, 0.50)
    safety_p90 = percentile(eta_under_errors, 0.90)
    safety_p95 = percentile(eta_under_errors, 0.95)

    conservative_convertible = [
        max(0.0, (as_float(r["queue_wait_at_station"]) or 0.0) - safety_p90)
        for r in audited_rows
        if r["missed_conversion_opportunity"] == "1"
    ]

    print(f"run_dir={run_dir}")
    print(f"decision_rows={len(rows)}")
    print(f"positive_hold_rows={positive_hold_rows}")
    print("release_reasons=" + ",".join(f"{k}:{v}" for k, v in sorted(release_reasons.items())))
    print(f"queue_wait_threshold={queue_wait_threshold:.3f}")
    print(f"queue_wait_rows={len(queue_waits)}")
    print(f"queue_wait_mean={mean(queue_waits):.6f}")
    print(f"queue_wait_p90={percentile(queue_waits, 0.90):.6f}")
    print(f"queue_wait_p95={percentile(queue_waits, 0.95):.6f}")
    print(f"missed_conversion_rows={missed_conversion_rows}")
    print(f"eta_underestimate_rows={len(eta_under_errors)}")
    print(f"eta_underestimate_mean={mean(eta_under_errors):.6f}")
    print(f"eta_underestimate_p50={safety_p50:.6f}")
    print(f"eta_underestimate_p90={safety_p90:.6f}")
    print(f"eta_underestimate_p95={safety_p95:.6f}")
    print(f"starvation_proxy_rows={starvation_proxy_rows}")
    print(f"conservative_convertible_after_p90_buffer_sum={sum(conservative_convertible):.6f}")
    print(f"conservative_convertible_after_p90_buffer_rows={sum(1 for v in conservative_convertible if v > 0.0)}")
    print("neighbor_buckets=")
    for bucket in sorted(bucket_stats):
        entry = bucket_stats[bucket]
        bucket_rows = int(entry["rows"])
        bucket_queue = entry["queue_waits"]  # type: ignore[assignment]
        bucket_under = entry["eta_under"]  # type: ignore[assignment]
        bucket_delay = entry["chosen_delays"]  # type: ignore[assignment]
        print(
            f"  {bucket}: rows={bucket_rows}, "
            f"queue_wait_mean={mean(bucket_queue):.6f}, "
            f"eta_under_mean={mean(bucket_under):.6f}, "
            f"chosen_delay_mean={mean(bucket_delay):.6f}"
        )

    if output_csv is not None:
        output_csv.parent.mkdir(parents=True, exist_ok=True)
        with output_csv.open("w", newline="") as fh:
            writer = csv.DictWriter(fh, fieldnames=list(audited_rows[0].keys()) if audited_rows else [])
            writer.writeheader()
            writer.writerows(audited_rows)
        print(f"audit_csv={output_csv}")

    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("run_dir", type=Path)
    parser.add_argument("--queue-wait-threshold", type=float, default=5.0)
    parser.add_argument("--output-csv", type=Path)
    args = parser.parse_args()
    return audit(args.run_dir, args.queue_wait_threshold, args.output_csv)


if __name__ == "__main__":
    raise SystemExit(main())
