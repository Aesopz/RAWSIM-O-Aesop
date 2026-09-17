# -*- coding: utf-8 -*-
"""Slide p27 replacement: HGS-M5 outer lambda loop + INNER BuildPlan (Canon v2: draws first,
dispatch only when no draw is negative, whole-order fallback when the split budget binds).
Deterministic matplotlib drawing in the deck's existing style (navy boxes, orange diamonds).

usage: python scripts/fig_m5_flowchart.py <out.png>
"""
import sys, os
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch, Polygon, FancyArrowPatch

NAVY, NAVY_FILL = "#1F3864", "#DEEAF6"
ORANGE, ORANGE_FILL = "#C55A11", "#FBE5D6"
GREY, GREY_FILL = "#7F7F7F", "#F2F2F2"
plt.rcParams.update({"font.family": "serif", "font.serif": ["Cambria", "Times New Roman", "DejaVu Serif"],
                     "pdf.fonttype": 42})

fig = plt.figure(figsize=(13.33, 6.6))
ax = fig.add_axes([0, 0, 1, 1]); ax.set_xlim(0, 133.3); ax.set_ylim(0, 66); ax.axis("off")

def box(x, y, w, h, title, sub=None, fill=NAVY_FILL, edge=NAVY, tcolor="black", fs=9.5, sfs=8, bold=True, r=0.6):
    ax.add_patch(FancyBboxPatch((x - w / 2, y - h / 2), w, h, boxstyle="round,pad=0,rounding_size=%g" % r,
                                fc=fill, ec=edge, lw=1.2, zorder=2))
    if sub:
        ax.text(x, y + 0.28 * h, title, ha="center", va="center", fontsize=fs, fontweight="bold" if bold else None, color=tcolor, zorder=3)
        ax.text(x, y - 0.22 * h, sub, ha="center", va="center", fontsize=sfs, color=tcolor, zorder=3)
    else:
        ax.text(x, y, title, ha="center", va="center", fontsize=fs, fontweight="bold" if bold else None, color=tcolor, zorder=3)

def diamond(x, y, w, h, text, fs=9):
    ax.add_patch(Polygon([(x - w / 2, y), (x, y + h / 2), (x + w / 2, y), (x, y - h / 2)], closed=True,
                         fc="white", ec=ORANGE, lw=1.4, zorder=2))
    ax.text(x, y, text, ha="center", va="center", fontsize=fs, fontweight="bold", color=NAVY, zorder=3)

def arrow(p, q, color=NAVY, lw=1.1, style="-|>", ms=9):
    ax.add_patch(FancyArrowPatch(p, q, arrowstyle=style, mutation_scale=ms, color=color, lw=lw, zorder=1,
                                 shrinkA=0, shrinkB=0))

def elbow(pts, color=NAVY, lw=1.1, head=True):
    for i in range(len(pts) - 1):
        last = i == len(pts) - 2
        arrow(pts[i], pts[i + 1], color=color, lw=lw, style="-|>" if (last and head) else "-")

def label(x, y, s, color=ORANGE, fs=8, ha="center", va="center", bold=False):
    ax.text(x, y, s, ha=ha, va=va, fontsize=fs, color=color, fontweight="bold" if bold else None, zorder=4)

# ───────────────────────── titles
ax.text(3, 63.5, "Proposed Heuristic Model : HGS-M5", fontsize=17, fontweight="bold", color=NAVY, va="center")
ax.text(3, 59.5, "OUTER  Updating λ", fontsize=9.5, color=GREY, va="center")
ax.text(50, 59.5, "INNER  BuildPlan(λ)", fontsize=9.5, color=NAVY, va="center", fontweight="bold")
ax.text(103, 59.5, "Move Scores", fontsize=9.5, color=GREY, va="center")
ax.add_patch(FancyBboxPatch((48.5, 3.5), 50, 54.2, boxstyle="round,pad=0,rounding_size=0.8",
                            fc="none", ec=GREY, lw=0.9, ls=(0, (4, 3)), zorder=0))

# ───────────────────────── OUTER loop (unchanged from M4G)
X = 20; W = 33
box(X, 56, W, 3.6, "λ ← λ₀", fill=ORANGE_FILL, edge=ORANGE, bold=False)
box(X, 50.5, W, 4.4, "plan ← BuildPlan(λ)", "the inner loop")
box(X, 44.5, W, 3.6, "P = (D* − objective) / λ", bold=False)
diamond(X, 38.5, 22, 5.0, "P ≤ 0 ?")
diamond(X, 31.5, 22, 5.0, "| objective | ≤ tol ?")
box(X, 25, W, 3.6, "λ_next = D* / P", bold=False)
diamond(X, 19, 22, 5.0, "λ_next ≤ 0 ?")
box(X, 12.8, W, 3.6, "trial ← BuildPlan(λ_next)", bold=False)
diamond(X, 6.8, 22, 5.0, "trial P ≤ 0 ?")
for a, b in ((54.2, 52.7), (48.3, 46.3), (42.7, 41.0), (36.0, 34.0), (29.0, 26.8), (23.2, 21.5), (16.5, 14.6), (11.0, 9.3)):
    arrow((X, a), (X, b))
for c in (38.5, 31.5, 19.0, 6.8):
    label(X + 1.0, c - 3.1, "no", GREY, ha="left")
# P<=0 -> jump to bound
diamond(43, 38.5, 12, 4.4, "λ̄ > λ ?", fs=8.5)
arrow((31, 38.5), (37, 38.5), color=ORANGE); label(33.5, 39.3, "yes")
elbow([(43, 40.7), (43, 50.5), (36.6, 50.5)], color=ORANGE); label(44.2, 45.5, "yes\nλ ← λ̄", ha="left", fs=7.5)
label(43.8, 35.6, "no", GREY, ha="left"); label(43, 34.0, "Stop: no feasible
non-empty plan", ORANGE, fs=7, bold=True)
# other stops
label(33.5, 32.3, "yes", ha="left"); label(40, 32.3, "Stop\nbreak-even", ORANGE, ha="left", va="center", fs=8, bold=True)
label(33.5, 19.8, "yes", ha="left"); label(40, 19.4, "Stop\nD* = 0, no dispatch", ORANGE, ha="left", va="center", fs=8, bold=True)
label(33.5, 7.6, "yes", ha="left"); label(40, 7.2, "Stop\nkeep previous plan", ORANGE, ha="left", va="center", fs=8, bold=True)
# adopt & loop back
elbow([(X, 4.3), (X, 2.2), (1.8, 2.2), (1.8, 50.5), (3.5, 50.5)])
label(2.8, 1.0, "No → adopt λ ← λ_next, re-run BuildPlan (cap 5)", GREY, ha="left", fs=7.5)

# ───────────────────────── INNER BuildPlan (Canon v2)
IX = 73.5; IW = 40
box(IX, 55.2, IW, 3.4, "Copy the working books", bold=True)
box(IX, 49.6, IW, 4.6, "①  Enumerate draw-line moves", "Station stock covers the line IN FULL · slot available")
diamond(IX, 43.3, 20, 4.6, "any draw Δ < 0 ?")
box(IX, 37.3, IW, 4.6, "②  Score every dispatch candidate", "Marginal: harvest WITH the pod − WITHOUT it", fill=ORANGE_FILL, edge=ORANGE)
diamond(IX, 31.0, 20, 4.6, "best dispatch Δ < 0 ?")
diamond(IX, 24.4, 24, 4.6, "split budget bound ?", fs=8.5)
box(IX, 18.2, IW, 4.6, "③  Whole-order fallback", "Cheapest pod set covering ONE unsplit order · any cover accepted", fill=ORANGE_FILL, edge=ORANGE)
box(IX, 11.4, IW, 4.6, "Valuation sweep", "Coverable but slot-blocked:  −λβ per line,  −μβ per order")
box(IX, 5.6, IW, 3.4, "return  plan  (D*, objective)", fill=GREY_FILL, edge=GREY, bold=False)
# main spine
arrow((IX, 53.5), (IX, 51.9)); arrow((IX, 47.3), (IX, 45.6))
arrow((IX, 41.0), (IX, 39.6)); label(IX + 0.9, 40.4, "no", GREY, ha="left")
arrow((IX, 35.0), (IX, 33.3))
arrow((IX, 28.7), (IX, 26.7)); label(IX + 0.9, 27.8, "no", GREY, ha="left")
arrow((IX, 22.1), (IX, 20.5)); label(IX + 0.9, 21.4, "yes", ORANGE, ha="left")
# "no" from the budget diamond: right, down, into the valuation sweep
NX = IX + 23.5
elbow([(IX + 12, 24.4), (NX, 24.4), (NX, 11.4), (IX + IW / 2, 11.4)]); label(NX - 0.6, 19.0, "no", GREY, ha="right")
arrow((IX, 9.1), (IX, 7.3))
# accept-and-loop edges (books changed → back to ①)
RX = IX + 21.5
elbow([(IX + 10, 43.3), (RX, 43.3), (RX, 49.6), (IX + IW / 2, 49.6)], color=ORANGE)
label(IX + 10.6, 44.3, "yes: take most negative draw", ORANGE, ha="left", fs=7.5)
elbow([(IX + 10, 31.0), (RX, 31.0), (RX, 43.3)], color=ORANGE, head=False)
label(IX + 10.6, 32.0, "yes: dispatch", ORANGE, ha="left", fs=7.5)
elbow([(IX - IW / 2, 18.2), (IX - 22.5, 18.2), (IX - 22.5, 49.6), (IX - IW / 2, 49.6)], color=ORANGE)
label(IX - 22.0, 34.5, "bundle accepted", ORANGE, ha="left", fs=7.5)
label(RX - 0.6, 46.6, "books changed", GREY, ha="right", fs=7.5)

# ───────────────────────── Move scores (right column)
SX = 115; SW = 32
box(SX, 51.5, SW, 9.0, "", fill="white", edge=NAVY)
ax.text(SX - SW / 2 + 1, 54.8, "DRAW from the committed pods in station", fontsize=8.5, fontweight="bold", color=NAVY, va="center")
ax.text(SX - SW / 2 + 1, 51.6, "Δ = −λ(1 − β)", fontsize=9.5, color=NAVY, va="center"); ax.text(SX + 4, 51.6, "Close an order-line", fontsize=7.5, color=ORANGE, va="center")
ax.text(SX - SW / 2 + 1, 48.6, "Δ = −(λ + μ)(1 − β)", fontsize=9.5, color=NAVY, va="center"); ax.text(SX + 4, 48.6, "Close an order", fontsize=7.5, color=ORANGE, va="center")
box(SX, 39.5, SW, 10.5, "", fill="white", edge=ORANGE)
ax.text(SX - SW / 2 + 1, 43.6, "DISPATCH a robot to fetch the new pod", fontsize=8.5, fontweight="bold", color=NAVY, va="center")
ax.text(SX - SW / 2 + 1, 40.6, "Δ = (d_r,p + d_p,w)", fontsize=9.5, color=NAVY, va="center"); ax.text(SX + 4, 40.6, "Extract travel distance", fontsize=7.5, color=ORANGE, va="center")
ax.text(SX - SW / 2 + 1, 38.0, "     + (U₊ − U₀)", fontsize=9.5, color=NAVY, va="center"); ax.text(SX + 4, 38.0, "Marginal unlock", fontsize=7.5, color=ORANGE, va="center")
ax.text(SX - SW / 2 + 1, 35.4, "     + (V₊ − V₀)", fontsize=9.5, color=NAVY, va="center"); ax.text(SX + 4, 35.4, "Valuation unlock", fontsize=7.5, color=ORANGE, va="center")
box(SX, 27.0, SW, 10.5, "", fill="white", edge=ORANGE)
ax.text(SX - SW / 2 + 1, 31.1, "WHOLE-ORDER fallback (budget bound)", fontsize=8.5, fontweight="bold", color=NAVY, va="center")
ax.text(SX - SW / 2 + 1, 28.1, "Δ = Σ (d_r,p + d_p,w)", fontsize=9.5, color=NAVY, va="center"); ax.text(SX + 4, 28.1, "One bot per pod", fontsize=7.5, color=ORANGE, va="center")
ax.text(SX - SW / 2 + 1, 25.5, "accept any cover, cheapest wins", fontsize=8.5, color=NAVY, va="center")
ax.text(SX - SW / 2 + 1, 23.0, "no box consumed · next decision re-enters ①", fontsize=7.5, color=GREY, va="center")

out = sys.argv[1] if len(sys.argv) > 1 else "fig_m5_flowchart.png"
fig.savefig(out, dpi=300); fig.savefig(out[:-4] + ".pdf")
print("->", out)
