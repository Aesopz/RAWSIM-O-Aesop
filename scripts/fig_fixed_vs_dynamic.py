# -*- coding: utf-8 -*-
"""APA 7 figures for the fixed-vs-dynamic pricing experiment (REPORTING-STANDARD 4.2).

Figure 1  Total travel distance vs items handled: one marker per policy, error bars = 95% CI
          across seeds, one panel per fleet size.
Figure 2  Dynamic pricing compared with each fixed exchange rate: horizontal bars of
          delta% = (fixed - dynamic)/dynamic on seven measures, one row per fleet size.
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

# ---------------------------------------------------------------- Figure 1
fig, axes = plt.subplots(1, 2, figsize=(10.5, 4.6), sharey=False)
plt.subplots_adjust(top=0.80, bottom=0.14, left=0.08, right=0.98, wspace=0.28)
for ax, bots in zip(axes, (6, 10)):
    dyn, fixed = arms_for(bots)
    # Labels: the high-lambda cluster (>= 11) overlaps, so those are listed once beside the cluster.
    cluster = [lab for lab in fixed if lam_of(lab) >= 11]
    for lab in fixed:
        x, y = per_seed(lab, "Items"), total_distance(lab)
        ax.errorbar(x.mean(), y.mean(), xerr=ci95(x), yerr=ci95(y), fmt="o", color=apa.BLACK, ms=5,
                    ecolor=apa.GREY, elinewidth=0.8, capsize=2, zorder=3)
        if lab not in cluster:
            ax.annotate("λ = %g" % lam_of(lab), (x.mean(), y.mean()), textcoords="offset points", xytext=(6, -3), fontsize=8)
    if cluster:
        cx = np.mean([per_seed(l, "Items").mean() for l in cluster]); cy = np.mean([total_distance(l).mean() for l in cluster])
        ax.annotate("λ = " + ", ".join("%g" % lam_of(l) for l in cluster), (cx, cy), textcoords="offset points",
                    xytext=(-8, 14), fontsize=8, ha="right", arrowprops=dict(arrowstyle="-", color=apa.GREY, lw=0.6))
    x, y = per_seed(dyn, "Items"), total_distance(dyn)
    ax.errorbar(x.mean(), y.mean(), xerr=ci95(x), yerr=ci95(y), fmt="D", mfc="white", mec=apa.BLACK, color=apa.BLACK,
                ms=7, ecolor=apa.BLACK, elinewidth=0.8, capsize=2, zorder=4)
    ax.set_xlabel("Items handled")
    ax.set_ylabel("Total travel distance (m)")
    ax.set_title("%d robots" % bots, fontsize=10, loc="left")
    ax.plot([], [], "o", color=apa.BLACK, label="Fixed exchange rate")
    ax.plot([], [], "D", mfc="white", mec=apa.BLACK, label="Dynamic exchange rate")
    ax.legend(loc="lower left", fontsize=8)
apa.furniture(fig, 1, "Total Travel Distance as a Function of Items Handled Under Fixed and Dynamic Exchange Rates",
              note="Error bars are 95% confidence intervals across 10 seeds. Fixed rates hold λ and μ constant with no "
                   "Dinkelbach iteration and no upper-bound jump; at λ = 4.5 several seeds stall, hence the wide interval. "
                   "Small instance, Canon v1.", top=0.97, gap=0.05, note_y=0.045)
apa.save(fig, os.path.join(out, "fig1_distance_vs_items.png"))
plt.close(fig)

# ---------------------------------------------------------------- Figure 2
measures = [("Items", "Items handled", 1), ("m/Line", "Distance per line", 1), ("Pile-on", "Pile-on", 1),
            ("EOR(kJ/order)", "Energy per order", 1), ("Trips", "Pod trips", 1), ("TurnoverMedian(s)", "Turnover time (median)", 1),
            ("StationIdle(%)", "Station idle", 1)]
fig, axes = plt.subplots(2, len(measures), figsize=(13.5, 5.6))
plt.subplots_adjust(top=0.82, bottom=0.12, left=0.06, right=0.99, wspace=0.55, hspace=0.55)
for r, bots in enumerate((6, 10)):
    dyn, fixed = arms_for(bots)
    lams = [lam_of(l) for l in fixed]
    for c, (key, title, _) in enumerate(measures):
        ax = axes[r, c]
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
        for yi, v, s in zip(y, vals, stars):
            # negative bars: label to the right of the zero line so it never collides with y ticks
            ax.text((v + lim * 0.03) if v >= 0 else lim * 0.03, yi, "%+.1f%s" % (v, s), va="center", ha="left", fontsize=7)
        ax.set_yticks(y); ax.set_yticklabels(["%g" % l for l in lams], fontsize=7)
        ax.set_xlim(-lim, lim)
        ax.set_title(title + (" (pp)" if key == "StationIdle(%)" else ""), fontsize=9)
        if c == 0: ax.set_ylabel("Fixed λ  (%d robots)" % bots, fontsize=8)
        if r == 1: ax.set_xlabel("Difference from dynamic (pp)" if key == "StationIdle(%)" else "Difference from dynamic (%)", fontsize=8)
        ax.tick_params(labelsize=7)
apa.furniture(fig, 2, "Fixed Exchange Rates Compared With Dynamic Pricing on Seven Measures",
              note="Bars show (fixed − dynamic) / dynamic × 100, paired by seed (n = 10). *p < .05. **p < .01. ***p < .001 "
                   "(two-tailed paired t tests). Station idle is reported in percentage points. Hatched bars: turnover, where lower is better for the fixed rate. Small instance, Canon v1.",
              top=0.975, gap=0.04, note_y=0.04)
apa.save(fig, os.path.join(out, "fig2_delta_by_measure.png"))
plt.close(fig)
print("figures ->", out)
