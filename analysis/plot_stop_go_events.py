#!/usr/bin/env python
"""Plot stop-and-go event positions from a RAWSim-O run.

Input:  stop_and_go_events.csv written by InstanceStatistics.
Output: stop_and_go_event_points.csv, stop_and_go_event_points.png,
        stop_and_go_locations.csv, and stop_and_go_locations.png by default.
"""

from __future__ import annotations

import argparse
import csv
from collections import defaultdict
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Plot stop-and-go event locations.")
    parser.add_argument("events_csv", help="Path to stop_and_go_events.csv")
    parser.add_argument(
        "--output-dir",
        default=None,
        help="Directory for generated files. Defaults to the events CSV directory.",
    )
    parser.add_argument("--title", default=None, help="Optional plot title.")
    parser.add_argument(
        "--annotate-top",
        type=int,
        default=12,
        help="Annotate the top N location/type buckets by event count.",
    )
    parser.add_argument(
        "--event-type",
        default=None,
        help="Only plot one event type, for example whca_planned_wait.",
    )
    parser.add_argument(
        "--annotate-events",
        action="store_true",
        help="Annotate every event point with its event index.",
    )
    return parser.parse_args()


def load_events(path: Path) -> list[dict[str, str]]:
    with path.open("r", newline="", encoding="utf-8-sig") as fh:
        rows = list(csv.DictReader(fh, delimiter=";"))
    required = {
        "type",
        "stop_node",
        "x",
        "y",
        "tier",
        "energy_kJ",
        "queue_terminal_node",
    }
    missing = sorted(required.difference(rows[0].keys() if rows else []))
    if missing:
        raise ValueError(f"Missing required columns in {path}: {', '.join(missing)}")
    return rows


def filter_events(rows: list[dict[str, str]], event_type: str | None) -> list[dict[str, str]]:
    if not event_type:
        return rows
    return [row for row in rows if row["type"] == event_type]


def write_event_points(path: Path, rows: list[dict[str, str]]) -> None:
    fieldnames = [
        "event_index",
        "time_sec",
        "type",
        "bot_id",
        "from_node",
        "stop_node",
        "to_node",
        "x",
        "y",
        "tier",
        "energy_kJ",
        "decel_kJ",
        "accel_kJ",
        "queue_terminal_node",
        "destination_node",
        "loaded",
    ]
    with path.open("w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(fh, fieldnames=fieldnames, delimiter=";")
        writer.writeheader()
        for index, row in enumerate(rows, start=1):
            out = dict(row)
            out["event_index"] = index
            writer.writerow({name: out.get(name, "") for name in fieldnames})


def aggregate(rows: list[dict[str, str]]) -> list[dict[str, object]]:
    buckets: dict[tuple[str, str, float, float, str, str], dict[str, object]] = {}
    for row in rows:
        key = (
            row["type"],
            row["stop_node"],
            float(row["x"]),
            float(row["y"]),
            row["tier"],
            row["queue_terminal_node"],
        )
        if key not in buckets:
            buckets[key] = {
                "type": row["type"],
                "stop_node": row["stop_node"],
                "x": float(row["x"]),
                "y": float(row["y"]),
                "tier": row["tier"],
                "queue_terminal_node": row["queue_terminal_node"],
                "count": 0,
                "energy_kJ": 0.0,
            }
        buckets[key]["count"] = int(buckets[key]["count"]) + 1
        buckets[key]["energy_kJ"] = float(buckets[key]["energy_kJ"]) + float(row["energy_kJ"])
    return sorted(
        buckets.values(),
        key=lambda r: (-int(r["count"]), str(r["type"]), int(r["stop_node"])),
    )


def write_summary(path: Path, rows: list[dict[str, object]]) -> None:
    fieldnames = [
        "type",
        "stop_node",
        "x",
        "y",
        "tier",
        "queue_terminal_node",
        "count",
        "energy_kJ",
    ]
    with path.open("w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(fh, fieldnames=fieldnames, delimiter=";")
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def plot_summary(path: Path, rows: list[dict[str, object]], title: str | None, annotate_top: int) -> None:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    colors = {
        "whca_planned_wait": "#d62728",
        "queue_manager_creep": "#1f77b4",
    }
    markers = {
        "whca_planned_wait": "o",
        "queue_manager_creep": "s",
    }
    grouped: dict[str, list[dict[str, object]]] = defaultdict(list)
    for row in rows:
        grouped[str(row["type"])].append(row)

    all_x = [float(r["x"]) for r in rows]
    all_y = [float(r["y"]) for r in rows]
    x_span = max(all_x) - min(all_x) if all_x else 1.0
    y_span = max(all_y) - min(all_y) if all_y else 1.0
    fig_height = max(2.8, min(7.0, 10.0 * max(y_span, 1.0) / max(x_span, 1.0) + 1.2))
    fig, ax = plt.subplots(figsize=(10, fig_height), constrained_layout=True)
    for event_type, group_rows in grouped.items():
        xs = [float(r["x"]) for r in group_rows]
        ys = [float(r["y"]) for r in group_rows]
        counts = [int(r["count"]) for r in group_rows]
        sizes = [35.0 + 24.0 * (c ** 0.75) for c in counts]
        ax.scatter(
            xs,
            ys,
            s=sizes,
            c=colors.get(event_type, "#666666"),
            marker=markers.get(event_type, "o"),
            alpha=0.72,
            edgecolors="#222222",
            linewidths=0.6,
            label=f"{event_type} ({sum(counts)})",
        )

    for row in rows[: max(0, annotate_top)]:
        ax.annotate(
            f"{row['stop_node']}:{row['count']}",
            (float(row["x"]), float(row["y"])),
            xytext=(4, 4),
            textcoords="offset points",
            fontsize=8,
        )

    ax.set_title(title or "Stop-and-go event locations")
    ax.set_xlabel("layout x")
    ax.set_ylabel("layout y")
    ax.set_aspect("equal", adjustable="box")
    ax.grid(True, linestyle=":", linewidth=0.7, alpha=0.45)
    ax.legend(loc="best")
    fig.savefig(path, dpi=180)
    plt.close(fig)


def plot_event_points(path: Path, rows: list[dict[str, str]], title: str | None, annotate_events: bool) -> None:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    colors = {
        "whca_planned_wait": "#d62728",
        "queue_manager_creep": "#1f77b4",
    }
    markers = {
        "whca_planned_wait": "o",
        "queue_manager_creep": "s",
    }
    all_x = [float(r["x"]) for r in rows]
    all_y = [float(r["y"]) for r in rows]
    x_span = max(all_x) - min(all_x) if all_x else 1.0
    y_span = max(all_y) - min(all_y) if all_y else 1.0
    fig_height = max(2.8, min(7.0, 10.0 * max(y_span, 1.0) / max(x_span, 1.0) + 1.2))
    fig, ax = plt.subplots(figsize=(10, fig_height), constrained_layout=True)

    grouped: dict[str, list[tuple[int, dict[str, str]]]] = defaultdict(list)
    for index, row in enumerate(rows, start=1):
        grouped[row["type"]].append((index, row))

    for event_type, indexed_rows in grouped.items():
        xs = [float(r["x"]) for _, r in indexed_rows]
        ys = [float(r["y"]) for _, r in indexed_rows]
        ax.scatter(
            xs,
            ys,
            s=46,
            c=colors.get(event_type, "#666666"),
            marker=markers.get(event_type, "o"),
            alpha=0.62,
            edgecolors="#222222",
            linewidths=0.5,
            label=f"{event_type} ({len(indexed_rows)})",
        )
        if annotate_events:
            for index, row in indexed_rows:
                ax.annotate(
                    str(index),
                    (float(row["x"]), float(row["y"])),
                    xytext=(4, 4),
                    textcoords="offset points",
                    fontsize=7,
                )

    ax.set_title(title or "Stop-and-go event points")
    ax.set_xlabel("layout x")
    ax.set_ylabel("layout y")
    ax.set_aspect("equal", adjustable="box")
    ax.grid(True, linestyle=":", linewidth=0.7, alpha=0.45)
    ax.legend(loc="best")
    fig.savefig(path, dpi=180)
    plt.close(fig)


def main() -> None:
    args = parse_args()
    events_csv = Path(args.events_csv)
    output_dir = Path(args.output_dir) if args.output_dir else events_csv.parent
    output_dir.mkdir(parents=True, exist_ok=True)

    rows = filter_events(load_events(events_csv), args.event_type)
    summary = aggregate(rows)
    points_path = output_dir / "stop_and_go_event_points.csv"
    points_plot_path = output_dir / "stop_and_go_event_points.png"
    summary_path = output_dir / "stop_and_go_locations.csv"
    plot_path = output_dir / "stop_and_go_locations.png"
    write_event_points(points_path, rows)
    plot_event_points(points_plot_path, rows, args.title, args.annotate_events)
    write_summary(summary_path, summary)
    plot_summary(plot_path, summary, args.title, args.annotate_top)

    print(f"events: {len(rows)}")
    print(f"locations: {len(summary)}")
    print(f"wrote: {points_path}")
    print(f"wrote: {points_plot_path}")
    print(f"wrote: {summary_path}")
    print(f"wrote: {plot_path}")


if __name__ == "__main__":
    main()
