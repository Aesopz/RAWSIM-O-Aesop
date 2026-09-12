# -*- coding: utf-8 -*-
import json, os, sys, math, statistics as st
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

BLUE, ORANGE, GREY, FAINT = "#2E63B0", "#C55A11", "#8A8F98", "#DCDFE3"
INK, MUTED = "#16181D", "#5B6B72"
CONT = {10: 15.6, 12: 8.9, 14: 4.0, 16: 1.9, 24: 0.0, 32: 0.0, 48: 0.0}

dyn = next(r for r in A if r["kind"] == "dyn")
qual = sorted([r for r in A if r["kind"] == "fx" and CONT[r["lam"]] <= 5],
              key=lambda r: r["lam"])

def ttest(m, arm):
    d = [a[m] - b[m] for a, b in zip(dyn["seeds"], arm["seeds"])]
    return st.mean(d) / (st.stdev(d) / math.sqrt(len(d)))

# (key, 標題, 方向, 冠軍註記)  hi = 越大越好
M = [("items", "完成件數", "hi"), ("dist", "機器人總行走距離", "lo"),
     ("pileon", "pile-on（每趟服務訂單）", "hi"), ("eor", "EOR（每單能耗）", "lo"),
     ("mpl", "公尺／行", "lo"), ("turn", "訂單完成週期（中位）", "lo")]

fig, axes = plt.subplots(2, 3, figsize=(14.6, 8.0), dpi=220)
fig.patch.set_facecolor("white")

for ax, (k, title, d) in zip(axes.ravel(), M):
    # 正值 = 動態較優，不論該指標方向
    vals, labs, cols, sigs = [], [], [], []
    for r in qual:
        rel = 100 * (dyn[k] - r[k]) / r[k]
        if d == "lo": rel = -rel
        t = ttest(k, r)
        vals.append(rel); labs.append("λ=%d" % r["lam"])
        cols.append(BLUE if rel > 0 else GREY)
        sigs.append("*" if abs(t) >= 2.776 else "")
    y = list(range(len(vals)))[::-1]
    ax.barh(y, vals, height=0.62, color=cols, zorder=3)
    ax.axvline(0, color=ORANGE, lw=2.2, zorder=4)

    lim = max(abs(v) for v in vals) * 1.62
    ax.set_xlim(-lim, lim)
    for yy, v, s in zip(y, vals, sigs):
        off = lim * 0.045
        ax.text(v + (off if v >= 0 else -off), yy, "%+.1f%%%s" % (v, s),
                va="center", ha="left" if v >= 0 else "right",
                fontsize=9.6, color=INK, fontweight="bold")
    ax.set_yticks(y); ax.set_yticklabels(labs, fontsize=10, color=MUTED)
    win = sum(1 for v in vals if v > 0)
    ax.set_title(title, fontsize=12, fontweight="bold", color=INK, pad=22, loc="left")
    ax.text(0, 1.02, "動態勝 %d / %d 個合格固定臂" % (win, len(vals)),
            transform=ax.transAxes, fontsize=9.2, va="bottom",
            color=BLUE if win == len(vals) else (GREY if win == 0 else MUTED))
    ax.set_xlabel("相對動態臂的差異（%）　右＝動態較優", fontsize=8.8,
                  color=MUTED, labelpad=5)
    ax.grid(axis="x", color=FAINT, lw=0.7, zorder=0); ax.set_axisbelow(True)
    for s_ in ("top", "right", "left"): ax.spines[s_].set_visible(False)
    ax.spines["bottom"].set_color(FAINT)
    ax.tick_params(colors=MUTED, labelsize=9, length=0)

fig.text(0.005, 0.978, "沒有一個固定值能同時贏兩項；動態一次拿下四項",
         fontsize=17.5, fontweight="bold", color=INK, va="top")
fig.text(0.005, 0.941,
         "M4G · 6 機器人 · 2 揀貨站 · 7200 s · 5 seeds　｜　橘線＝動態臂（基準 0）　｜　"
         "藍＝動態較優，灰＝動態較劣　｜　* 為配對 t 檢定 p<.05（df=4）",
         fontsize=9.5, color=MUTED, va="top")
fig.subplots_adjust(left=0.045, right=0.988, top=0.845, bottom=0.075,
                    wspace=0.30, hspace=0.46)
out = os.path.join(SP, "dyn_vs_champions.png")
fig.savefig(out, facecolor="white"); print(out)
