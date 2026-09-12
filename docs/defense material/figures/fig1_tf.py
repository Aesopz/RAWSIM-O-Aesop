# -*- coding: utf-8 -*-
"""Figure 1 (true fixed-rate policies). Total travel distance against items handled."""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import apa
import matplotlib.pyplot as plt

SP = sys.argv[1]
A = json.load(open(os.path.join(SP, "arms_tf.json")))
apa.apply_style(base=10)
dyn, fixed = A["dyn"], sorted(A["fixed"], key=lambda r: r["lam"])

fig, ax = plt.subplots(figsize=(6.5, 4.3))
for r in fixed:
    ax.errorbar(r["items"], r["dist"], xerr=r["items_sd"], yerr=r["dist_sd"],
                fmt="o", ms=5.5, mfc=apa.BLACK, mec=apa.BLACK, ecolor=apa.GREY,
                elinewidth=0.8, capsize=2.5, capthick=0.8, zorder=3)
ax.errorbar(dyn["items"], dyn["dist"], xerr=dyn["items_sd"], yerr=dyn["dist_sd"],
            fmt="D", ms=6.5, mfc="white", mec=apa.BLACK, mew=1.4, ecolor=apa.BLACK,
            elinewidth=1.0, capsize=2.5, capthick=1.0, zorder=4)
for r in fixed:
    ax.annotate("\u03bb = %g" % r["lam"], (r["items"], r["dist"]),
                textcoords="offset points", xytext=(0, 10), ha="center", fontsize=8.5)

best = min(fixed, key=lambda r: r["dist"])
gap = best["dist"] - dyn["dist"]
xg = max(r["items"] for r in fixed + [dyn]) + 6
ax.plot([dyn["items"], xg], [dyn["dist"]]*2, color=apa.LGREY, lw=0.6, zorder=1)
ax.plot([best["items"], xg], [best["dist"]]*2, color=apa.LGREY, lw=0.6, zorder=1)
ax.annotate("", xy=(xg, dyn["dist"]), xytext=(xg, best["dist"]),
            arrowprops=dict(arrowstyle="<->", color=apa.BLACK, lw=0.9, shrinkA=0, shrinkB=0))
ax.text(xg+1.0, (dyn["dist"]+best["dist"])/2, "\u2212%.0f m\n(\u2212%.1f%%)" % (gap, 100*gap/best["dist"]),
        fontsize=8.5, va="center", linespacing=1.4)

ax.set_xlabel("Items handled")
ax.set_ylabel("Total travel distance (m)")
h1 = ax.plot([], [], "o", ms=5.5, mfc=apa.BLACK, mec=apa.BLACK, ls="none")[0]
h2 = ax.plot([], [], "D", ms=6.5, mfc="white", mec=apa.BLACK, mew=1.4, ls="none")[0]
ax.legend([h1, h2], ["Fixed exchange rate", "Dynamic exchange rate"],
          loc="lower left", handletextpad=0.6, borderpad=0.2, labelspacing=0.5)
fig.subplots_adjust(left=0.115, right=0.90, top=0.845, bottom=0.135)
apa.furniture(fig, "1",
  "Total Travel Distance as a Function of Items Handled Under Fixed and Dynamic Exchange Rates",
  None, top=0.982, gap=0.058)
print(apa.save(fig, os.path.join(SP, "fig1_tf.png")))
