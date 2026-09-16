# -*- coding: utf-8 -*-
"""APA 7th-edition figure furniture for matplotlib.

APA 7 (sections 7.22-7.36) puts four elements around the image: a bold flush-left
figure number, an italic title in title case, the image itself, and a note that
begins with an italic "Note." Everything inside the image is sans serif, 8-14 pt,
with no gridlines, no 3-D, and no decoration that does not carry information.
Colour is permitted but the figure must survive greyscale reproduction, so series
are separated by marker shape and fill as well as by value.

The note is optional here: pass note=None to emit only the number and the title,
leaving the caption to be written alongside the figure in the manuscript.
"""
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

BLACK, GREY, LGREY = "#000000", "#595959", "#BFBFBF"

def apply_style(base=10):
    plt.rcParams.update({
        "font.family": "sans-serif",
        "font.sans-serif": ["Arial", "Helvetica", "DejaVu Sans"],
        "font.size": base,
        "axes.labelsize": base,
        "axes.titlesize": base,
        "xtick.labelsize": base - 1,
        "ytick.labelsize": base - 1,
        "legend.fontsize": base - 1,
        "axes.edgecolor": BLACK,
        "axes.linewidth": 0.8,
        "axes.grid": False,
        "axes.spines.top": False,
        "axes.spines.right": False,
        "xtick.direction": "out",
        "ytick.direction": "out",
        "xtick.major.width": 0.8,
        "ytick.major.width": 0.8,
        "xtick.major.size": 3.5,
        "ytick.major.size": 3.5,
        "xtick.color": BLACK,
        "ytick.color": BLACK,
        "text.color": BLACK,
        "axes.labelcolor": BLACK,
        "figure.facecolor": "white",
        "savefig.facecolor": "white",
        "legend.frameon": False,
        "axes.unicode_minus": False,
        "pdf.fonttype": 42,      # embed TrueType, not Type 3 - required by most journals
        "ps.fonttype": 42,
    })

def furniture(fig, number, title, note=None, top=0.955, gap=0.042, note_y=0.055, x=0.012):
    """Figure number (bold), title (italic, title case), and an optional Note block."""
    fig.text(x, top, "Figure %s" % number, fontsize=10, fontweight="bold", va="top")
    fig.text(x, top - gap, title, fontsize=10, fontstyle="italic", va="top")
    if note:
        fig.text(x, note_y, r"$\it{Note.}$ " + note, fontsize=9, va="top", linespacing=1.45)

def save(fig, path_png):
    fig.savefig(path_png, dpi=600)
    fig.savefig(path_png[:-4] + ".pdf")
    return path_png


# ------------------------------------------------------------------ collision-free labels
def _segments_hit(bbox, segments, pad=2.0):
    """True if any display-space segment [(x0,y0),(x1,y1)] passes through bbox (expanded by pad px)."""
    import numpy as _np
    bb = bbox.expanded(1.0, 1.0).padded(pad)
    for (x0, y0), (x1, y1) in segments:
        for t in _np.linspace(0.0, 1.0, 64):
            if bb.contains(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t):
                return True
    return False


def place_labels(ax, points, obstacles=(), fontsize=8, leader=True):
    """Annotate `points` = [(x, y, text)] so that no label overlaps an error bar, marker, legend
    or another label (REPORTING-STANDARD 4.2 rule 10).

    obstacles: display-space segments [((x0,y0),(x1,y1))] — pass every error-bar arm and marker
    footprint.  Candidate offsets spiral outward; the first collision-free one wins.  If a label
    had to travel far it gets a thin grey leader line back to its point.
    """
    fig = ax.figure
    fig.canvas.draw()
    rend = fig.canvas.get_renderer()
    placed = []               # bboxes of labels already placed
    for leg in [ax.get_legend()] + list(fig.legends):
        if leg is not None:
            placed.append(leg.get_window_extent(rend))
    axbb = ax.get_window_extent(rend)
    dirs = [(1, 1), (1, -1), (-1, 1), (-1, -1), (0, 1), (0, -1), (1, 0), (-1, 0)]
    rings = [8, 14, 22, 32, 44, 58, 74]
    for (x, y, text) in points:
        best = None
        for r in rings:
            for dx, dy in dirs:
                ha = "left" if dx > 0 else ("right" if dx < 0 else "center")
                va = "bottom" if dy > 0 else ("top" if dy < 0 else "center")
                t = ax.annotate(text, (x, y), textcoords="offset points", xytext=(dx * r, dy * r),
                                fontsize=fontsize, ha=ha, va=va, zorder=6)
                bb = t.get_window_extent(rend)
                ok = axbb.contains(bb.x0, bb.y0) and axbb.contains(bb.x1, bb.y1) \
                    and not any(bb.overlaps(p.padded(2)) for p in placed) \
                    and not _segments_hit(bb, obstacles)
                if ok:
                    best = (t, bb, r); break
                t.remove()
            if best: break
        if best is None:      # give up gracefully: far upper-right with a leader
            t = ax.annotate(text, (x, y), textcoords="offset points", xytext=(60, 60), fontsize=fontsize,
                            ha="left", va="bottom", zorder=6,
                            arrowprops=dict(arrowstyle="-", color=GREY, lw=0.6))
            best = (t, t.get_window_extent(rend), 999)
        t, bb, r = best
        if leader and r > 14:
            t.arrow_patch = None
            t.set_position(t.get_position())
            t.arrowprops = dict(arrowstyle="-", color=GREY, lw=0.6)
            # matplotlib needs the arrow at construction time: rebuild once.
            xy_off = t.get_position(); t.remove()
            t = ax.annotate(text, (x, y), textcoords="offset points", xytext=xy_off, fontsize=fontsize,
                            ha=t.get_ha(), va=t.get_va(), zorder=6,
                            arrowprops=dict(arrowstyle="-", color=GREY, lw=0.6, shrinkB=2))
            bb = t.get_window_extent(rend)
        placed.append(bb)
        obstacles = list(obstacles) + [((bb.x0, bb.y0), (bb.x1, bb.y1)), ((bb.x0, bb.y1), (bb.x1, bb.y0))]
    return placed


def errorbar_segments(ax, x, y, xerr, yerr):
    """Display-space segments of one error-bar cross plus a small marker footprint, for place_labels."""
    tr = ax.transData.transform
    segs = [(tuple(tr((x - xerr, y))), tuple(tr((x + xerr, y)))),
            (tuple(tr((x, y - yerr))), tuple(tr((x, y + yerr))))]
    px, py = tr((x, y))
    segs.append(((px - 5, py - 5), (px + 5, py + 5))); segs.append(((px - 5, py + 5), (px + 5, py - 5)))
    return segs
