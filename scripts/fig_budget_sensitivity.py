# -*- coding: utf-8 -*-
"""Split-budget sensitivity figures (F1–F11): one figure per budget axis.

Each figure: three panels (Items handled, Distance per line, Energy per order) with the budget
level on the x axis (categorical, ascending; "∞" last) and 95% CI error bars over seeds.
Reads stats.json of the experiment; arm labels are matched by regex.

usage: python scripts/fig_budget_sensitivity.py <experiment_id> <out_dir> <base_arm> <axis> <regex> <xlabel> <title>
  axis     : name used in the file name, e.g. n / pk
  regex    : capture group 1 = numeric level, e.g. "m5_pure_n(\\d+)_45b"
example:
  python scripts/fig_budget_sensitivity.py 2026-09-18_large_pure_m5_split_limit_and_packing out m5_pure_45b n "m5_pure_n(\\d+)_45b" "Maximum parts per order" "Split Limit Sensitivity (HGS-M5, 45 Robots)"
"""
import sys, os, io, json, re, math
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "defense material", "figures"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import apa
import matplotlib.pyplot as plt
from scipy import stats as sps
import exp as E

eid, out, base_arm, axis, rx, xlabel, title = sys.argv[1:8]
os.makedirs(out, exist_ok=True)
d = E.exp_dir(eid)
S = json.load(io.open(os.path.join(d, "stats.json"), encoding="utf-8"))
apa.apply_style(base=10)
PROV = {"experiment": eid, "canon_version": E.current_canon_version_checked()["version"],
        "runs": sorted(r["run_id"] for r in E.registry_rows() if r["experiment"] == eid and r["validity"] == "valid"),
        "sources": [os.path.relpath(os.path.join(d, "stats.json"), E.ROOT)]}

def per_seed(label, metric):
    return np.array([S["per_seed"][label][s][metric] for s in sorted(S["per_seed"][label], key=int)], dtype=float)

def ci95(x):
    return sps.t.ppf(0.975, x.size - 1) * x.std(ddof=1) / math.sqrt(x.size)

levels = sorted(((int(m.group(1)), lab) for lab in S["per_seed"] for m in [re.fullmatch(rx, lab)] if m), key=lambda t: t[0])
labels = [lab for _, lab in levels] + [base_arm]
ticks = ["%d" % lv for lv, _ in levels] + ["∞"]
measures = [("Items", "Items handled"), ("m/Line", "Distance per line (m)"), ("EOR(kJ/order)", "Energy per order (kJ)")]

fig, axes = plt.subplots(1, 3, figsize=(10.5, 3.6))
plt.subplots_adjust(top=0.84, bottom=0.18, left=0.08, right=0.98, wspace=0.42)
for ax, (key, ylab) in zip(axes, measures):
    means = [per_seed(l, key).mean() for l in labels]; cis = [ci95(per_seed(l, key)) for l in labels]
    x = np.arange(len(labels))
    ax.errorbar(x, means, yerr=cis, fmt="o-", color=apa.BLACK, ms=5, lw=0.9, ecolor=apa.GREY, elinewidth=0.8, capsize=2)
    ax.set_xticks(x); ax.set_xticklabels(ticks)
    ax.set_xlabel(xlabel); ax.set_ylabel(ylab)
    ax.margins(x=0.10, y=0.10)
    # stars from the paired test against the unlimited base, placed above each non-base point
    fig.canvas.draw()
    for xi, lab in zip(x[:-1], labels[:-1]):
        P = S["paired"].get("%s vs %s" % (lab, base_arm), {}).get(key)
        if P and P["label"] in ("*", "**", "***"):
            ax.annotate(P["label"], (xi, means[xi] + cis[xi]), textcoords="offset points", xytext=(0, 4), ha="center", fontsize=9)
apa.furniture(fig, None, title, note=None, top=0.975)
apa.save(fig, os.path.join(out, "fig_%s_sensitivity.png" % axis), provenance=PROV)
plt.close(fig)
print("figure ->", out)
