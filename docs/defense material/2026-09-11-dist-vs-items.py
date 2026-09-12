# -*- coding: utf-8 -*-
import json, os, sys
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager

SP = sys.argv[1]
A = json.load(open(os.path.join(SP, "arms.json")))
have = {f.name for f in font_manager.fontManager.ttflist}
cjk = next((c for c in ["Microsoft JhengHei", "Microsoft YaHei", "SimHei"] if c in have), None)
plt.rcParams["font.family"] = "sans-serif"
plt.rcParams["font.sans-serif"] = ([cjk] if cjk else []) + ["DejaVu Sans"]
plt.rcParams["axes.unicode_minus"] = False

BLUE, ORANGE, FAINT = "#2E63B0", "#C55A11", "#DCDFE3"
INK, MUTED = "#16181D", "#5B6B72"
QUAL = (14, 16, 24, 32, 48)          # 跳躍觸發 5% 以下才是乾淨的固定臂

dyn = next(r for r in A if r["kind"] == "dyn")
fix = sorted([r for r in A if r["kind"] == "fx" and r["lam"] in QUAL],
             key=lambda r: r["lam"])

fig, ax = plt.subplots(figsize=(9.0, 6.2), dpi=220)
fig.patch.set_facecolor("white")

for r in fix:
    ax.errorbar(r["items"], r["dist"], xerr=r["items_sd"], yerr=r["dist_sd"],
                fmt="o", ms=8, color=BLUE, ecolor=BLUE, elinewidth=1.2,
                capsize=0, alpha=0.85, zorder=3)
ax.errorbar(dyn["items"], dyn["dist"], xerr=dyn["items_sd"], yerr=dyn["dist_sd"],
            fmt="*", ms=22, color=ORANGE, ecolor=ORANGE, elinewidth=1.6,
            capsize=0, zorder=5)

OFF = {14: (-26, 4), 16: (-26, -12), 24: (0, 13), 32: (0, 13), 48: (0, 13)}
for r in fix:
    dx, dy = OFF[r["lam"]]
    ax.annotate("λ=%d" % r["lam"], (r["items"], r["dist"]), textcoords="offset points",
                xytext=(dx, dy), ha="center", fontsize=10, color=BLUE)
ax.annotate("動態", (dyn["items"], dyn["dist"]), textcoords="offset points",
            xytext=(0, -26), ha="center", fontsize=12, color=ORANGE, fontweight="bold")

best = min(fix, key=lambda r: r["dist"])
gap = best["dist"] - dyn["dist"]
ax.annotate("", xy=(dyn["items"], dyn["dist"]), xytext=(dyn["items"], best["dist"]),
            arrowprops=dict(arrowstyle="-", color=ORANGE, lw=1.4, ls=(0, (4, 3))))
ax.text(dyn["items"] + 0.55, (dyn["dist"] + best["dist"]) / 2,
        "-%.0f m" % gap, color=ORANGE, fontsize=11.5, fontweight="bold", va="center")

ax.set_xlabel("完成件數", fontsize=11, color=MUTED, labelpad=8)
ax.set_ylabel("機器人總行走距離（公尺）", fontsize=11, color=MUTED, labelpad=8)
ax.grid(color=FAINT, lw=0.7, zorder=0); ax.set_axisbelow(True)
for s in ("top", "right"): ax.spines[s].set_visible(False)
for s in ("left", "bottom"): ax.spines[s].set_color(FAINT)
ax.tick_params(colors=MUTED, labelsize=10, length=3)

fig.text(0.005, 0.972, "同樣的件數，少走 562 公尺", fontsize=17, fontweight="bold",
         color=INK, va="top")
fig.text(0.005, 0.928,
         "M4G · 6 機器人 · 2 揀貨站 · 7200 s · 5 seeds · 誤差線為標準差　｜　"
         "藍點為五個合格固定 λ",
         fontsize=9.5, color=MUTED, va="top")
fig.subplots_adjust(left=0.108, right=0.982, top=0.858, bottom=0.10)
out = os.path.join(SP, "dist_vs_items.png")
fig.savefig(out, facecolor="white"); print(out)
