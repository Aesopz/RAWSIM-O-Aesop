# -*- coding: utf-8 -*-
"""Paired-difference bar figure (F1–F12): one row of panels, one panel per measure, one bar per
comparison. Bars are (b − a)/a in %, station idle in percentage points; stars from the paired t test.

usage: python scripts/fig_paired_bars.py <experiment_id> <out.png> "<title>" <b1>:<a1>:<row label> [<b2>:<a2>:<row label> ...]
"""
import sys, os, io, json
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "defense material", "figures"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import apa
import matplotlib.pyplot as plt
import exp as E

eid, out, title = sys.argv[1], sys.argv[2], sys.argv[3]
rows = [tuple(x.split(":")) for x in sys.argv[4:]]
os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
d = E.exp_dir(eid)
S = json.load(io.open(os.path.join(d, "stats.json"), encoding="utf-8"))
apa.apply_style(base=10)
PROV = {"experiment": eid, "canon_version": E.current_canon_version_checked()["version"],
        "runs": sorted(r["run_id"] for r in E.registry_rows() if r["experiment"] == eid and r["validity"] == "valid"),
        "sources": [os.path.relpath(os.path.join(d, "stats.json"), E.ROOT)]}
# F12: energy per order stands in for distance; distance column omitted.
measures = [("Items", "Items handled"), ("Pile-on", "Pile-on"), ("EOR(kJ/order)", "Energy per order"),
            ("Trips", "Pod trips"), ("TurnoverMedian(s)", "Turnover time (median)"), ("StationIdle(%)", "Station idle (pp)")]

fig, axes = plt.subplots(1, len(measures), figsize=(12.0, 1.6 + 0.55 * len(rows)))
plt.subplots_adjust(top=0.78, bottom=0.30, left=0.10, right=0.99, wspace=0.55)
y = np.arange(len(rows))[::-1]
for c, (key, mtitle) in enumerate(measures):
    ax = axes[c]
    vals, stars = [], []
    for b, a, _ in rows:
        P = S["paired"]["%s vs %s" % (b, a)][key]
        vals.append(P["mean_diff"] if key == "StationIdle(%)" else P["pct"])
        stars.append(P["label"] if P["label"] in ("*", "**", "***") else "")
    ax.barh(y, vals, color=apa.GREY if key != "TurnoverMedian(s)" else "white", edgecolor=apa.BLACK, linewidth=0.6,
            hatch="//" if key == "TurnoverMedian(s)" else None, height=0.6)
    ax.axvline(0, color=apa.BLACK, linewidth=0.8)
    lim = max(abs(min(vals)), abs(max(vals))) * 1.9 + 3
    for yi, v, s_ in zip(y, vals, stars):
        ax.text((v + lim * 0.03) if v >= 0 else lim * 0.03, yi, "%+.1f%s" % (v, s_), va="center", ha="left", fontsize=7)
    ax.set_yticks(y); ax.set_yticklabels([r[2] for r in rows] if c == 0 else [""] * len(rows), fontsize=8)
    ax.set_xlim(-lim, lim); ax.set_title(mtitle, fontsize=9)
    ax.set_xlabel("Difference (pp)" if key == "StationIdle(%)" else "Difference (%)", fontsize=8)
    ax.tick_params(labelsize=7)
apa.furniture(fig, None, title, note=None, top=0.97)
apa.save(fig, out, provenance=PROV)
print("figure ->", out)
