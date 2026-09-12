# -*- coding: utf-8 -*-
"""Figure 2 (true fixed-rate policies). Seven measures, dynamic vs each admissible fixed lambda."""
import json, os, sys, math, statistics as st
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import apa
import matplotlib.pyplot as plt

SP = sys.argv[1]
A = json.load(open(os.path.join(SP, "arms_tf.json")))
apa.apply_style(base=10)
dyn, fixed = A["dyn"], A["fixed"]
NS = dyn["n"]
TCRIT = {3: 3.182, 4: 2.776, 9: 2.262}.get(NS-1, 2.0)

def paired_t(m, a):
    d = [x[m]-y[m] for x, y in zip(dyn["seeds"], a["seeds"])]
    sd = st.stdev(d)
    return st.mean(d)/(sd/math.sqrt(len(d))) if sd else float("nan")

PANELS = [("items", "Items handled", "hi"), ("dist", "Travel distance", "lo"),
          ("pileon", "Pile-on", "hi"), ("eor", "Energy per order", "lo"),
          ("mpl", "Distance per line", "lo"),
          ("turn", "Turnover time (median)", "lo"),
          ("late", "Late orders", "lo")]

fig, axes = plt.subplots(2, 4, figsize=(6.5, 4.0))
for ax in axes.ravel()[len(PANELS):]:
    ax.axis("off")

for ax, (k, title, direction) in zip(axes.ravel(), PANELS):
    if k == "late":
        # Counts, not percentages: the policies differ by well under one order out of ~600,
        # and a relative scale would inflate that into a double-digit-looking effect.
        names = ["Dynamic"] + ["%g" % r["lam"] for r in fixed]
        vals  = [dyn["late"]] + [r["late"] for r in fixed]
        sds   = [dyn["late_sd"]] + [r["late_sd"] for r in fixed]
        faces = ["white"] + ["#4D4D4D"]*len(fixed)
        y = list(range(len(vals)))[::-1]
        ax.barh(y, vals, height=0.6, color=faces, edgecolor=apa.BLACK, linewidth=0.7, zorder=3)
        ax.errorbar(vals, y, xerr=sds, fmt="none", ecolor=apa.BLACK,
                    elinewidth=0.7, capsize=2, capthick=0.7, zorder=4)
        for yy, v, sd in zip(y, vals, sds):
            ax.text(v + sd + max(vals)*0.06, yy, "%.1f" % v, va="center", fontsize=6.5)
        ax.set_xlim(0, max(v+sd for v, sd in zip(vals, sds)) * 1.35)
        ax.set_yticks(y); ax.set_yticklabels(names, fontsize=7)
        ax.set_title(title, fontsize=8, pad=3)
        ax.tick_params(axis="y", length=0); ax.tick_params(axis="x", labelsize=6.5)
        ax.spines["left"].set_visible(False)
        ax.set_xlabel("Late orders (count); all n.s.", fontsize=7, labelpad=3)
        continue
    vals, labs, faces, hatches, stars = [], [], [], [], []
    for r in fixed:
        base = r[k]
        rel = 100*(dyn[k]-base)/base if base else 0.0
        if direction == "lo": rel = -rel
        t = paired_t(k, r)
        vals.append(rel); labs.append("%g" % r["lam"])
        faces.append("#4D4D4D" if rel > 0 else "white")
        hatches.append("" if rel > 0 else "////")
        stars.append("*" if (t == t and abs(t) >= TCRIT) else "")
    y = list(range(len(vals)))[::-1]
    bars = ax.barh(y, vals, height=0.6, color=faces, edgecolor=apa.BLACK, linewidth=0.7, zorder=3)
    for b, h in zip(bars, hatches):
        if h: b.set_hatch(h)
    ax.axvline(0, color=apa.BLACK, lw=0.9, zorder=4)
    lim = max(abs(v) for v in vals) * 1.85 or 1.0
    ax.set_xlim(-lim, lim); ax.set_ylim(-0.7, len(vals)-0.3)
    for yy, v, s in zip(y, vals, stars):
        off = lim*0.05
        ax.text(v + (off if v >= 0 else -off), yy, "%+.1f%s" % (v, s), va="center",
                ha="left" if v >= 0 else "right", fontsize=6.5)
    ax.set_yticks(y); ax.set_yticklabels(labs, fontsize=7)
    ax.set_title(title, fontsize=8, pad=3)
    ax.tick_params(axis="y", length=0); ax.tick_params(axis="x", labelsize=6.5)
    ax.spines["left"].set_visible(False)

for ax in axes[:, 0]:
    ax.set_ylabel("Fixed λ", fontsize=7.5, labelpad=2)
for ax in (axes[1, 0], axes[1, 1], axes[0, 3]):
    ax.set_xlabel("Difference from dynamic (%)", fontsize=7, labelpad=3)

fig.subplots_adjust(left=0.062, right=0.988, top=0.805, bottom=0.105, wspace=0.50, hspace=0.62)
apa.furniture(fig, "2",
  "Dynamic Pricing Compared With Each Admissible Fixed Exchange Rate on Seven Measures",
  None, top=0.982, gap=0.062)
print(apa.save(fig, os.path.join(SP, "fig2_tf.png")))
