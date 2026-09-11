#!/usr/bin/env python3
"""Validate and summarize slow-start decision traces for a RAWSim-O run."""

from __future__ import annotations

import argparse
import csv
import re
from collections import Counter
from pathlib import Path


REQUIRED_COLUMNS = [
    "time",
    "bot_id",
    "task_id",
    "pod_id",
    "station_id",
    "eta_now",
    "t_starve",
    "slack",
    "chosen_delay",
    "deadline",
    "probe_success",
    "release_reason",
    "station_pending_items",
    "station_busy_remaining",
    "station_queue_work",
    "eta_at_release",
    "remaining_t_starve_at_release",
    "neighbor_count_at_decision",
    "missing_arrival_count",
    "queue_arrival_time",
    "release_time",
    "actual_arrival_time",
    "actual_eta",
    "arrival_error",
    "station_idle_at_arrival",
    "queue_wait_at_station",
]


def parse_int_counter(stat_text: str, name: str) -> int:
    total = 0
    for match in re.finditer(rf"^{re.escape(name)}:\s+(\d+)\s*$", stat_text, re.MULTILINE):
        total += int(match.group(1))
    return total


def as_float(value: str) -> float | None:
    if value == "":
        return None
    return float(value)


def validate(run_dir: Path) -> int:
    trace_path = run_dir / "slowstart_decisions.csv"
    stats_path = run_dir / "statistics.txt"
    if not trace_path.exists():
        raise FileNotFoundError(trace_path)
    if not stats_path.exists():
        raise FileNotFoundError(stats_path)

    with trace_path.open(newline="") as fh:
        reader = csv.DictReader(fh)
        rows = list(reader)
        columns = reader.fieldnames or []

    missing = [col for col in REQUIRED_COLUMNS if col not in columns]
    if missing:
        raise ValueError("missing trace columns: " + ", ".join(missing))

    stat_text = stats_path.read_text(encoding="utf-8", errors="replace")
    stat_decisions = parse_int_counter(stat_text, "StatSlowStartDecisionCount")
    stat_immediate = parse_int_counter(stat_text, "StatSlowStartImmediateReleaseCount")
    stat_failures = parse_int_counter(stat_text, "StatSlowStartSearchFailures")

    release_reasons = Counter(row["release_reason"] for row in rows)
    probe_failures = sum(1 for row in rows if row["probe_success"] == "0")
    positive_holds = sum(1 for row in rows if (as_float(row["chosen_delay"]) or 0.0) > 0.0)
    actual_arrivals = sum(1 for row in rows if row["actual_arrival_time"] != "")
    queue_arrivals = sum(1 for row in rows if row["queue_arrival_time"] != "")
    arrival_errors = [as_float(row["arrival_error"]) for row in rows if row["arrival_error"] != ""]
    queue_waits = [as_float(row["queue_wait_at_station"]) for row in rows if row["queue_wait_at_station"] != ""]

    errors: list[str] = []
    if len(rows) != stat_decisions:
        errors.append(f"decision row count {len(rows)} != stats decision count {stat_decisions}")
    if release_reasons.get("immediate_release", 0) != stat_immediate:
        errors.append(
            "immediate release rows "
            f"{release_reasons.get('immediate_release', 0)} != stats immediate count {stat_immediate}"
        )
    if probe_failures != stat_failures:
        errors.append(f"probe failure rows {probe_failures} != stats search failure count {stat_failures}")

    print(f"run_dir={run_dir}")
    print(f"decision_rows={len(rows)}")
    print(f"stats_decision_count={stat_decisions}")
    print(f"positive_hold_rows={positive_holds}")
    print(f"actual_arrival_rows={actual_arrivals}")
    print(f"queue_arrival_rows={queue_arrivals}")
    print(f"probe_failure_rows={probe_failures}")
    print("release_reasons=" + ",".join(f"{k}:{v}" for k, v in sorted(release_reasons.items())))
    if arrival_errors:
        abs_errors = [abs(v) for v in arrival_errors if v is not None]
        print(f"arrival_error_abs_mean={sum(abs_errors) / len(abs_errors):.6f}")
        print(f"arrival_error_abs_max={max(abs_errors):.6f}")
    if queue_waits:
        valid_waits = [v for v in queue_waits if v is not None]
        print(f"queue_wait_mean={sum(valid_waits) / len(valid_waits):.6f}")
        print(f"queue_wait_max={max(valid_waits):.6f}")

    if errors:
        print("status=FAIL")
        for error in errors:
            print("error=" + error)
        return 1

    print("status=PASS")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("run_dir", type=Path)
    args = parser.parse_args()
    return validate(args.run_dir)


if __name__ == "__main__":
    raise SystemExit(main())
