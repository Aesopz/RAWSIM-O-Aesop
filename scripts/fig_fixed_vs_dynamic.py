# -*- coding: utf-8 -*-
"""APA 7 figures for the fixed-vs-dynamic pricing experiment (REPORTING-STANDARD 4.2).

Figure A  Total travel distance vs items handled: one marker per policy, error bars = 95% CI
          across seeds. One figure per fleet size (figA_*_6b / _10b).
Figure B  Dynamic pricing compared with each fixed exchange rate: horizontal bars of
          delta% = (fixed - dynamic)/dynamic on seven measures. One figure per fleet size.
All numbers come from the experiment's stats.json (scipy + Excel cross-checked).

usage: python scripts/fig_fixed_vs_dynamic.py <experiment_id> <out_dir>
"""
import sys, os, io, json, re, math
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "defense material", "figures"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import apa
import matplotlib.pyplot as plt
from scipy import stats as sps
import exp as E

eid, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
d = E.exp_dir(eid)
S = json.load(io.open(os.path.join(d, "stats.json"), encoding="utf-8"))
ex = E.load_exp(eid)
apa.apply_style(base=10)
# F10 provenance: every run this figure is built from
PROV = {"experiment": eid, "canon_version": E.current_canon_version_checked()["version"],
        "runs": sorted(r["run_id"] for r in E.registry_rows() if r["experiment"] == eid and r["validity"] == "valid"),
        "sources": [os.path.relpath(os.path.join(d, "stats.json"), E.ROOT)]}

def arms_for(bots):
    dyn = "m4g_dyn_%db" % bots
    fixed = sorted([a["label"] for a in ex["arms"] if a["label"].endswith("_%db" % bots) and "fix" in a["label"]],
                   key=lambda l: float(re.search(r"fix([0-9p]+)_", l).group(1).replace("p", ".")))
    return dyn, fixed

def lam_of(label):
    return float(re.search(r"fix([0-9p]+)_", label).group(1).replace("p", "."))

def per_seed(label, metric):
    return np.array([S["per_seed"][label][s][metric] for s in sorted(S["per_seed"][label], key=int)], dtype=float)

def total_distance(label):
    # m/line * lines, per seed
    return per_seed(label, "m/Line") * per_seed(label, "Lines")

def ci95(x):
    n = x.size
    return sps.t.ppf(0.975, n - 1) * x.std(ddof=1) / math.sqrt(n)

# ---------------------------------------------------------------- Figure A: distance vs items, one figure per fleet
for bots in (6, 10):
    dyn, fixed = arms_for(bots)
    fig, ax = plt.subplots(figsize=(5.6, 4.6))
    plt.subplots_adjust(top=0.90, bottom=0.12, left=0.15, right=0.97)
    pts = {lab: (per_seed(lab, "Items").mean(), total_distance(lab).mean()) for lab in fixed}
    xs = [p[0] for p in pts.values()]; ys = [p[1] for p in pts.values()]
    sx, sy = (max(xs) - min(xs)) or 1, (max(ys) - min(ys)) or 1
    groups = []
    for lab in fixed:
        for g in groups:
            if any(abs(pts[lab][0] - pts[o][0]) / sx < 0.03 and abs(pts[lab][1] - pts[o][1]) / sy < 0.06 for o in g):
                g.append(lab); break
        else:
            groups.append([lab])
    for lab in fixed:
        x, y = per_seed(lab, "Items"), total_distance(lab)
        ax.errorbar(x.mean(), y.mean(), xerr=ci95(x), yerr=ci95(y), fmt="o", color=apa.BLACK, ms=5,
                    ecolor=apa.GREY, elinewidth=0.8, capsize=2, zorder=3)
    x, y = per_seed(dyn, "Items"), total_distance(dyn)
    ax.errorbar(x.mean(), y.mean(), xerr=ci95(x), yerr=ci95(y), fmt="D", mfc="white", mec=apa.BLACK, color=apa.BLACK,
                ms=7, ecolor=apa.BLACK, elinewidth=0.8, capsize=2, zorder=4)
    ax.set_xlabel("Items handled")
    ax.set_ylabel("Total travel distance (m)")
    ax.margins(x=0.10, y=0.10)
    # legend: the upper-left quadrant is empty in both fleets (low-lambda points sit bottom-left, high-lambda top-right)
    h = [plt.Line2D([], [], marker="o", ls="", color=apa.BLACK, label="Fixed exchange rate"),
         plt.Line2D([], [], marker="D", ls="", mfc="white", mec=apa.BLACK, label="Dynamic exchange rate")]
    ax.legend(handles=h, loc="upper left", fontsize=8, frameon=False)
    fig.canvas.draw()
    obstacles = []
    for lab in fixed + [dyn]:
        xx, yy = per_seed(lab, "Items"), total_distance(lab)
        obstacles += apa.errorbar_segments(ax, xx.mean(), yy.mean(), ci95(xx), ci95(yy))
    labels = []
    for g in groups:
        cx = np.mean([pts[l][0] for l in g]); cy = np.mean([pts[l][1] for l in g])
        labels.append((cx, cy, "λ = " + ", ".join("%g" % lam_of(l) for l in g)))
    apa.place_labels(ax, labels, obstacles)
    apa.furniture(fig, None, "Fixed vs. Dynamic Exchange Rate: Distance and Throughput (%d Robots)" % bots, note=None, top=0.975)
    apa.save(fig, os.path.join(out, "figA_distance_vs_items_%db.png" % bots), provenance=PROV)
    plt.close(fig)

# ---------------------------------------------------------------- Figure B: delta on seven measures, one figure per fleet
measures = [("Items", "Items handled", 1), ("m/Line", "Distance per line", 1), ("Pile-on", "Pile-on", 1),
            ("EOR(kJ/order)", "Energy per order", 1), ("Trips", "Pod trips", 1), ("TurnoverMedian(s)", "Turnover time (median)", 1),
            ("StationIdle(%)", "Station idle", 1)]
for bots in (6, 10):
    dyn, fixed = arms_for(bots)
    lams = [lam_of(l) for l in fixed]
    fig, axes = plt.subplots(1, len(measures), figsize=(13.5, 3.2))
    plt.subplots_adjust(top=0.80, bottom=0.18, left=0.06, right=0.99, wspace=0.55)
    for c, (key, title, _) in enumerate(measures):
        ax = axes[c]
        vals, stars = [], []
        for lab in fixed:
            P = S["paired"]["%s vs %s" % (lab, dyn)][key]
            # Station idle is itself a percentage: report the difference in percentage POINTS, not %.
            v = P["mean_diff"] if key == "StationIdle(%)" else P["pct"]
            vals.append(v); stars.append(P["label"] if P["label"] in ("*", "**", "***") else "")
        y = np.arange(len(fixed))[::-1]
        ax.barh(y, vals, color=apa.GREY if key != "TurnoverMedian(s)" else "white",
                edgecolor=apa.BLACK, linewidth=0.6, hatch="//" if key == "TurnoverMedian(s)" else None, height=0.65)
        ax.axvline(0, color=apa.BLACK, linewidth=0.8)
        lim = max(abs(min(vals)), abs(max(vals))) * 1.9 + 5
        for yi, v, s_ in zip(y, vals, stars):
            # negative bars: label to the right of the zero line so it never collides with y ticks
            ax.text((v + lim * 0.03) if v >= 0 else lim * 0.03, yi, "%+.1f%s" % (v, s_), va="center", ha="left", fontsize=7)
        ax.set_yticks(y); ax.set_yticklabels(["%g" % l for l in lams], fontsize=7)
        ax.set_xlim(-lim, lim)
        ax.set_title(title + (" (pp)" if key == "StationIdle(%)" else ""), fontsize=9)
        if c == 0: ax.set_ylabel("Fixed λ", fontsize=8)
        ax.set_xlabel("Difference from dynamic (pp)" if key == "StationIdle(%)" else "Difference from dynamic (%)", fontsize=8)
        ax.tick_params(labelsize=7)
    apa.furniture(fig, None, "Fixed vs. Dynamic Exchange Rate: Seven Measures (%d Robots)" % bots, note=None, top=0.97)
    apa.save(fig, os.path.join(out, "figB_delta_by_measure_%db.png" % bots), provenance=PROV)
    plt.close(fig)
print("figures ->", out)
