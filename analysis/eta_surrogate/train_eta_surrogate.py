#!/usr/bin/env python
"""Train small supervised ETA surrogate models from RAWSimO Playground CSV output.

The script trains separate MLP regressors for:
  - bot_to_pod
  - pod_to_station_queue

For each kind it compares:
  - cheap features: geometry/topology available without path search
  - path features: cheap features plus offline path descriptors
"""

from __future__ import annotations

import argparse
import csv
import math
import pickle
from pathlib import Path

import numpy as np
from sklearn.metrics import mean_absolute_error, mean_squared_error, r2_score
from sklearn.model_selection import GroupShuffleSplit, train_test_split
from sklearn.neural_network import MLPRegressor
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler


CHEAP_FEATURES = [
    "loaded",
    "orientation_bucket",
    "from_x",
    "from_y",
    "to_x",
    "to_y",
    "abs_dx",
    "abs_dy",
    "euclid",
    "manhattan",
    "same_tier",
    "from_storage",
    "to_storage",
    "from_queue",
    "to_queue",
    "from_degree",
    "to_degree",
]

PATH_FEATURES = CHEAP_FEATURES + [
    "path_hops",
    "path_distance",
    "path_turns",
    "path_segments",
]


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


def matrix(rows: list[dict[str, str]], feature_names: list[str]) -> tuple[np.ndarray, np.ndarray]:
    x = np.array([[parse_float(row[name]) for name in feature_names] for row in rows], dtype=float)
    y = np.array([parse_float(row["eta_sec"]) for row in rows], dtype=float)
    return x, y


def metrics(y_true: np.ndarray, y_pred: np.ndarray) -> dict[str, float]:
    err = y_pred - y_true
    abs_err = np.abs(err)
    under = y_true - y_pred
    under_pos = under[under > 0]
    return {
        "n": float(len(y_true)),
        "mae": float(mean_absolute_error(y_true, y_pred)),
        "rmse": float(mean_squared_error(y_true, y_pred) ** 0.5),
        "r2": float(r2_score(y_true, y_pred)) if len(y_true) > 1 else float("nan"),
        "p50_abs": float(np.percentile(abs_err, 50)),
        "p90_abs": float(np.percentile(abs_err, 90)),
        "p95_abs": float(np.percentile(abs_err, 95)),
        "mean_bias_pred_minus_actual": float(np.mean(err)),
        "under_rate": float(np.mean(err < 0.0)),
        "p90_under_margin": float(np.percentile(under_pos, 90)) if len(under_pos) else 0.0,
        "max_under": float(np.max(under_pos)) if len(under_pos) else 0.0,
    }


def leg_group_key(row: dict[str, str]) -> str:
    if row["kind"] == "bot_to_pod":
        return "|".join(
            [
                row["kind"],
                row["source_scope"],
                row["bot_id"],
                row["from_id"],
                row["to_id"],
                row["pod_id"],
                row["orientation_bucket"],
            ]
        )
    return "|".join(
        [
            row["kind"],
            row["from_id"],
            row["to_id"],
            row["pod_id"],
            row["station_id"],
        ]
    )


def split_train_test(
    x: np.ndarray, y: np.ndarray, rows: list[dict[str, str]]
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray, int]:
    groups = np.array([leg_group_key(row) for row in rows])
    unique_groups = len(set(groups))
    if unique_groups >= 2:
        splitter = GroupShuffleSplit(n_splits=1, test_size=0.2, random_state=42)
        train_idx, test_idx = next(splitter.split(x, y, groups))
        return x[train_idx], x[test_idx], y[train_idx], y[test_idx], unique_groups

    x_train, x_test, y_train, y_test = train_test_split(x, y, test_size=0.2, random_state=42)
    return x_train, x_test, y_train, y_test, unique_groups


def train_one(rows: list[dict[str, str]], kind: str, feature_set_name: str, feature_names: list[str], out_dir: Path) -> dict[str, float]:
    kind_rows = [r for r in rows if r["kind"] == kind]
    if len(kind_rows) < 20:
        raise ValueError(f"Not enough samples for {kind}: {len(kind_rows)}")

    x, y = matrix(kind_rows, feature_names)
    x_train, x_test, y_train, y_test, unique_groups = split_train_test(x, y, kind_rows)

    model = make_pipeline(
        StandardScaler(),
        MLPRegressor(
            hidden_layer_sizes=(32, 16),
            activation="relu",
            solver="adam",
            alpha=1e-4,
            learning_rate_init=1e-3,
            max_iter=2000,
            early_stopping=True,
            n_iter_no_change=30,
            random_state=42,
        ),
    )
    model.fit(x_train, y_train)
    pred = model.predict(x_test)
    result = metrics(y_test, pred)
    result["train_n"] = float(len(y_train))
    result["unique_leg_groups"] = float(unique_groups)

    model_path = out_dir / f"{kind}_{feature_set_name}_mlp.pkl"
    with model_path.open("wb") as f:
        pickle.dump({"model": model, "features": feature_names, "kind": kind}, f)
    result["model_path"] = str(model_path)

    pred_path = out_dir / f"{kind}_{feature_set_name}_predictions.csv"
    with pred_path.open("w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["actual_eta_sec", "pred_eta_sec", "error_pred_minus_actual"])
        for actual, p in zip(y_test, pred):
            writer.writerow([f"{actual:.9g}", f"{p:.9g}", f"{(p - actual):.9g}"])
    result["prediction_path"] = str(pred_path)
    return result


def write_report(results: list[tuple[str, str, dict[str, float]]], out_dir: Path, sample_csv: Path) -> None:
    report = out_dir / "eta_surrogate_report.md"
    lines = [
        "# ETA Surrogate Training Report",
        "",
        f"Sample CSV: `{sample_csv}`",
        "",
        "| kind | features | unique leg groups | test n | MAE | RMSE | R2 | P90 abs | under rate | P90 under margin | max under |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for kind, feature_set, m in results:
        lines.append(
            "| {kind} | {feature_set} | {unique_leg_groups:.0f} | {n:.0f} | {mae:.3f} | {rmse:.3f} | {r2:.3f} | "
            "{p90_abs:.3f} | {under_rate:.3f} | {p90_under_margin:.3f} | {max_under:.3f} |".format(
                kind=kind, feature_set=feature_set, **m
            )
        )
    lines.extend(
        [
            "",
            "Interpretation:",
            "- `under_rate` is the share of test samples where the model predicted too low.",
            "- `p90_under_margin` is a candidate safety buffer to add when underestimation is risky.",
            "- Train/test split is grouped by leg identity, so repeated visit rows for the same physical leg do not leak into both sets.",
            "- Use the cheap model only if its P90 absolute error and underestimation margin are acceptable for station-starvation trigger timing.",
        ]
    )
    report.write_text("\n".join(lines) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("sample_csv", type=Path)
    parser.add_argument("out_dir", type=Path)
    args = parser.parse_args()

    args.out_dir.mkdir(parents=True, exist_ok=True)
    rows = load_rows(args.sample_csv)
    if not rows:
        raise SystemExit("No finite samples loaded.")

    results: list[tuple[str, str, dict[str, float]]] = []
    for kind in sorted({row["kind"] for row in rows}):
        for feature_set_name, feature_names in [("cheap", CHEAP_FEATURES), ("path", PATH_FEATURES)]:
            result = train_one(rows, kind, feature_set_name, feature_names, args.out_dir)
            results.append((kind, feature_set_name, result))
            print(
                f"{kind:22s} {feature_set_name:5s} "
                f"MAE={result['mae']:.3f} RMSE={result['rmse']:.3f} "
                f"P90abs={result['p90_abs']:.3f} under={result['under_rate']:.3f} "
                f"P90under={result['p90_under_margin']:.3f}"
            )

    write_report(results, args.out_dir, args.sample_csv)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
