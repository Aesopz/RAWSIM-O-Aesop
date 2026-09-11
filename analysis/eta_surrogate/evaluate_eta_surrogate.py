#!/usr/bin/env python
"""Evaluate trained RAWSimO ETA surrogate models on a held-out CSV."""

from __future__ import annotations

import argparse
import csv
import math
import pickle
from pathlib import Path

import numpy as np
from sklearn.metrics import mean_absolute_error, mean_squared_error, r2_score


def parse_float(value: str) -> float:
    if value in {"inf", "Infinity"}:
        return math.inf
    if value in {"-inf", "-Infinity"}:
        return -math.inf
    if value == "nan":
        return math.nan
    return float(value)


def load_rows(path: Path) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    with path.open(newline="") as f:
        for row in csv.DictReader(f):
            eta = parse_float(row["eta_sec"])
            if math.isfinite(eta):
                rows.append(row)
    return rows


def percentile(values: np.ndarray, q: float) -> float:
    return float(np.percentile(values, q)) if len(values) else 0.0


def evaluate_kind(rows: list[dict[str, str]], model_path: Path, buffer_sec: float, out_dir: Path) -> dict[str, float | str]:
    with model_path.open("rb") as f:
        payload = pickle.load(f)
    model = payload["model"]
    feature_names = payload["features"]
    kind = payload["kind"]

    kind_rows = [row for row in rows if row["kind"] == kind]
    if not kind_rows:
        raise ValueError(f"No rows for kind {kind}")

    x = np.array([[parse_float(row[name]) for name in feature_names] for row in kind_rows], dtype=float)
    y = np.array([parse_float(row["eta_sec"]) for row in kind_rows], dtype=float)
    pred = model.predict(x)
    pred_buffered = pred + buffer_sec

    err = pred - y
    err_buffered = pred_buffered - y
    abs_err = np.abs(err)
    abs_err_buffered = np.abs(err_buffered)
    under = y - pred
    under_pos = under[under > 0.0]
    under_buffered = y - pred_buffered
    under_buffered_pos = under_buffered[under_buffered > 0.0]

    pred_path = out_dir / f"{kind}_path_external_predictions.csv"
    with pred_path.open("w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow([
            "visit_id",
            "actual_eta_sec",
            "pred_eta_sec",
            "buffer_sec",
            "pred_plus_buffer_sec",
            "error_pred_minus_actual",
            "error_buffered_minus_actual",
            "under_margin_sec",
            "buffered_under_margin_sec",
        ])
        for row, actual, p, pb, e, eb, u, ub in zip(kind_rows, y, pred, pred_buffered, err, err_buffered, under, under_buffered):
            writer.writerow([
                row.get("visit_id", ""),
                f"{actual:.9g}",
                f"{p:.9g}",
                f"{buffer_sec:.9g}",
                f"{pb:.9g}",
                f"{e:.9g}",
                f"{eb:.9g}",
                f"{max(0.0, u):.9g}",
                f"{max(0.0, ub):.9g}",
            ])

    return {
        "kind": kind,
        "n": float(len(y)),
        "buffer_sec": float(buffer_sec),
        "actual_mean": float(np.mean(y)),
        "actual_p50": percentile(y, 50),
        "actual_p90": percentile(y, 90),
        "mae": float(mean_absolute_error(y, pred)),
        "rmse": float(mean_squared_error(y, pred) ** 0.5),
        "r2": float(r2_score(y, pred)) if len(y) > 1 else float("nan"),
        "signed_bias": float(np.mean(err)),
        "abs_p50": percentile(abs_err, 50),
        "abs_p75": percentile(abs_err, 75),
        "abs_p90": percentile(abs_err, 90),
        "abs_p95": percentile(abs_err, 95),
        "abs_p99": percentile(abs_err, 99),
        "abs_max": float(np.max(abs_err)),
        "under_rate": float(np.mean(err < 0.0)),
        "under_p90": percentile(under_pos, 90),
        "under_max": float(np.max(under_pos)) if len(under_pos) else 0.0,
        "buffered_under_rate": float(np.mean(err_buffered < 0.0)),
        "buffered_under_p90": percentile(under_buffered_pos, 90),
        "buffered_under_max": float(np.max(under_buffered_pos)) if len(under_buffered_pos) else 0.0,
        "buffered_abs_p50": percentile(abs_err_buffered, 50),
        "buffered_abs_p90": percentile(abs_err_buffered, 90),
        "prediction_path": str(pred_path),
    }


def write_report(results: list[dict[str, float | str]], sample_csv: Path, out_dir: Path) -> None:
    lines = [
        "# External ETA Surrogate Evaluation",
        "",
        f"Sample CSV: `{sample_csv}`",
        "",
        "| kind | n | buffer | MAE | RMSE | R2 | P90 abs | under rate | max under | buffered under rate | buffered max under |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for r in results:
        lines.append(
            "| {kind} | {n:.0f} | {buffer_sec:.3f} | {mae:.3f} | {rmse:.3f} | {r2:.3f} | "
            "{abs_p90:.3f} | {under_rate:.3f} | {under_max:.3f} | {buffered_under_rate:.3f} | {buffered_under_max:.3f} |".format(**r)
        )
    lines.extend([
        "",
        "Buffered underestimation is computed as `actual - (prediction + buffer)` when positive.",
    ])
    (out_dir / "external_eta_surrogate_report.md").write_text("\n".join(lines) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("sample_csv", type=Path)
    parser.add_argument("model_dir", type=Path)
    parser.add_argument("out_dir", type=Path)
    parser.add_argument("--bot-buffer", type=float, default=2.0)
    parser.add_argument("--station-buffer", type=float, default=1.0)
    args = parser.parse_args()

    args.out_dir.mkdir(parents=True, exist_ok=True)
    rows = load_rows(args.sample_csv)
    results = [
        evaluate_kind(rows, args.model_dir / "bot_to_pod_path_mlp.pkl", args.bot_buffer, args.out_dir),
        evaluate_kind(rows, args.model_dir / "pod_to_station_queue_path_mlp.pkl", args.station_buffer, args.out_dir),
    ]
    write_report(results, args.sample_csv, args.out_dir)
    for r in results:
        print(
            "{kind:22s} n={n:.0f} MAE={mae:.3f} P90abs={abs_p90:.3f} "
            "under={under_rate:.3f} maxUnder={under_max:.3f} "
            "bufferedUnder={buffered_under_rate:.3f} bufferedMaxUnder={buffered_under_max:.3f}".format(**r)
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
