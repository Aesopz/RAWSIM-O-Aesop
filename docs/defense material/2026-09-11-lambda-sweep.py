import json, os, sys
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager

SP = sys.argv[1]
D = json.load(open(os.path.join(SP, "sweep.json")))

have = {f.name for f in font_manager.fontManager.ttflist}
cjk = next((c for c in ["Microsoft JhengHei", "Microsoft YaHei", "SimHei"] if c in have), None)
plt.rcParams["font.family"] = "sans-serif"
plt.rcParams["font.sans-serif"] = ([cjk] if cjk else []) + ["DejaVu Sans"]
plt.rcParams["axes.unicode_minus"] = False

BLUE, ORANGE, GREY, FAINT = "#2E63B0", "#C55A11", "#8A8F98", "#DCDFE3"
INK, MUTED = "#16181D", "#5B6B72"

F   = D["fixed"]
dyn = D["dyn"]
lam = [r["lam"] for r in F]
X   = list(range(len(lam)))
ok  = [r["cont"] <= 5 for r in F]
XD  = len(lam) + 0.55                      # 動態臂的標記位置（軸內最右）

fig, axes = plt.subplots(1, 3, figsize=(14.4, 5.0), dpi=220)
fig.patch.set_facecolor("white")

def style(ax, title, sub, ylab):
    ax.set_title(title, fontsize=13, fontweight="bold", color=INK, pad=30, loc="left")
    ax.text(0, 1.028, sub, transform=ax.transAxes, fontsize=9.2, color=MUTED, va="bottom")
    ax.set_ylabel(ylab, fontsize=10, color=MUTED, labelpad=7)
    ax.set_xlim(-0.6, len(lam) + 1.15)
    ax.set_xticks(X + [XD])
    ax.set_xticklabels([str(v) for v in lam] + ["動態"], fontsize=9.5)
    for t, good in zip(ax.get_xticklabels()[:-1], ok):
        t.set_color(MUTED if good else GREY)
    ax.get_xticklabels()[-1].set_color(ORANGE)
    ax.get_xticklabels()[-1].set_fontweight("bold")
    ax.set_xlabel("固定匯率 λ（公尺／行）", fontsize=10, color=MUTED, labelpad=6)
    ax.axvspan(1.6, 4.4, color=BLUE, alpha=0.06, zorder=0, lw=0)
    ax.grid(axis="y", color=FAINT, lw=0.7, zorder=0)
    ax.set_axisbelow(True)
    for s in ("top", "right"): ax.spines[s].set_visible(False)
    for s in ("left", "bottom"): ax.spines[s].set_color(FAINT)
    ax.tick_params(colors=MUTED, labelsize=9.5, length=3)

def series(ax, y, err=None):
    ax.plot(X, y, color=BLUE, lw=2, zorder=3, solid_capstyle="round")
    if err:
        ax.errorbar(X, y, yerr=err, fmt="none", ecolor=BLUE, elinewidth=1.1,
                    capsize=3, capthick=1.1, alpha=0.6, zorder=3)
    for x, v, good in zip(X, y, ok):
        ax.plot(x, v, "o", ms=8.5, zorder=4,
                mfc=BLUE if good else "white", mec=BLUE, mew=0 if good else 1.8)

def dynref(ax, v, sd=None, label_above=True):
    ax.axhline(v, color=ORANGE, lw=1.8, ls=(0, (5, 3)), zorder=2)
    if sd:
        ax.errorbar([XD], [v], yerr=[sd], fmt="none", ecolor=ORANGE,
                    elinewidth=1.1, capsize=3, capthick=1.1, alpha=0.6, zorder=4)
    ax.plot(XD, v, "*", ms=17, color=ORANGE, zorder=5)

# ── 面板一：吞吐持平 ───────────────────────────────────────────────
ax = axes[0]
style(ax, "吞吐持平", "完成件數 · 5 seeds · 誤差槓為標準差", "完成件數")
series(ax, [r["items"] for r in F], [r["items_sd"] for r in F])
dynref(ax, dyn["items"], dyn["items_sd"])
ax.set_ylim(1378, 1434)
ax.text(XD, 1421, "動態臂落在\n固定臂的雲團裡", color=ORANGE, fontsize=9,
        ha="right", va="bottom", linespacing=1.35)

# ── 面板二：主結果 ────────────────────────────────────────────────
ax = axes[1]
style(ax, "每行距離：動態臂不在曲線上", "每關掉一行所走的取貨距離（公尺）", "公尺／行")
series(ax, [r["mpl"] for r in F], [r["mpl_sd"] for r in F])
dynref(ax, dyn["mpl"], dyn["mpl_sd"])
best_i = min((i for i in X if ok[i]), key=lambda i: F[i]["mpl"])
by = F[best_i]["mpl"]
ax.annotate("", xy=(best_i, dyn["mpl"]), xytext=(best_i, by),
            arrowprops=dict(arrowstyle="<->", color=ORANGE, lw=1.5, shrinkA=0, shrinkB=0))
ax.text(best_i + 0.22, (dyn["mpl"] + by) / 2, "-6.1%\np < .05", color=ORANGE,
        fontsize=10, fontweight="bold", va="center", linespacing=1.35)
ax.set_ylim(9.25, 10.72)

# ── 面板三：合格性 ────────────────────────────────────────────────
ax = axes[2]
style(ax, "為何下緣停在 λ = 14", "由上界跳躍決定的派車佔比", "跳躍佔派車比例（%）")
series(ax, [r["cont"] for r in F])
ax.axhline(5, color=GREY, lw=1.4, ls=(0, (3, 3)), zorder=2)
ax.text(len(lam) - 0.1, 5.7, "5% 合格線", color=GREY, fontsize=9.2, ha="right")
ax.text(1.55, 30.5, "λ < 14 的臂有相當比例\n不是靠自己的常數在跑，\n而是靠保底跳躍", color=MUTED,
        fontsize=9.2, va="top", linespacing=1.45)
ax.plot(XD, dyn["cont"], "*", ms=17, color=ORANGE, zorder=5)
ax.text(XD, dyn["cont"] + 1.2, "32.8%", color=ORANGE, fontsize=9.2,
        fontweight="bold", ha="center")
ax.set_ylim(-1.6, 38)

fig.text(0.006, 0.982, "沒有任何固定匯率追得上量測出來的匯率",
         fontsize=17, fontweight="bold", color=INK, va="top")
fig.text(0.006, 0.930,
         "M4G · 6 機器人 · 2 揀貨站 · 7200 s · 5 seeds　｜　實心＝合格（跳躍 5% 以下），空心＝不合格　｜　"
         "網底 λ = 14 ~ 24：三個合格點彼此無統計差異",
         fontsize=9.6, color=MUTED, va="top")
fig.subplots_adjust(left=0.052, right=0.992, top=0.755, bottom=0.108, wspace=0.235)
out = os.path.join(SP, "lambda_sweep.png")
fig.savefig(out, facecolor="white")
print(out)
