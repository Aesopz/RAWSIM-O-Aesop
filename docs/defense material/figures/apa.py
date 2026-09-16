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
    """Title (italic, title case) and an optional Note block. `number=None` (the default for
    slide figures, F11) omits the "Figure N" label: numbering belongs to the document that places
    the figure, not to the image. Pass a number only when the user asks for one."""
    if number is not None:
        fig.text(x, top, "Figure %s" % number, fontsize=10, fontweight="bold", va="top")
        top -= gap
    if len(title.split()) > 10:
        raise AssertionError("F11: figure title must be a short topic name (<= 10 words), got %d words" % len(title.split()))
    fig.text(x, top, title, fontsize=10, fontstyle="italic", va="top")
    if note:
        fig.text(x, note_y, r"$\it{Note.}$ " + note, fontsize=9, va="top", linespacing=1.45)

def check_axes(fig, max_margin=0.10):
    """F6/F7 guard. For every axes: disable offset text / scientific offset (the usual reason a
    point at y=10 'reads' as 11), verify every plotted datum lies inside the axis limits, and
    report the tick step so the reader can confirm ticks are 1/2/5 x 10^n. Raises on violations."""
    import numpy as _np
    problems, report = [], []
    for ax in fig.axes:
        if not ax.get_visible() or ax.get_legend_handles_labels() == ([], []) and not ax.lines and not ax.collections and not ax.patches:
            continue
        try:
            ax.ticklabel_format(useOffset=False, style="plain")
        except Exception:
            pass                      # categorical axes have no ScalarFormatter
        xl, yl = ax.get_xlim(), ax.get_ylim()
        xs, ys = [], []
        for ln in ax.lines:
            x, y = ln.get_xdata(orig=False), ln.get_ydata(orig=False)
            xs += list(_np.atleast_1d(x)); ys += list(_np.atleast_1d(y))
        from matplotlib.collections import PathCollection as _PC, LineCollection as _LC
        for c in ax.collections:
            if isinstance(c, _PC):                       # scatter points
                off = c.get_offsets()
                if len(off): xs += list(off[:, 0]); ys += list(off[:, 1])
            elif isinstance(c, _LC):                     # error-bar arms
                for seg in c.get_segments():
                    xs += list(seg[:, 0]); ys += list(seg[:, 1])
        for pt in ax.patches:
            bb = pt.get_bbox() if hasattr(pt, "get_bbox") else None
            if bb is not None: xs += [bb.x0, bb.x1]; ys += [bb.y0, bb.y1]
        xs = [v for v in xs if _np.isfinite(v)]; ys = [v for v in ys if _np.isfinite(v)]
        if xs and (min(xs) < min(xl) or max(xs) > max(xl)): problems.append("%s: data x in [%g, %g] outside limits [%g, %g]" % (ax.get_title() or "axes", min(xs), max(xs), xl[0], xl[1]))
        if ys and (min(ys) < min(yl) or max(ys) > max(yl)): problems.append("%s: data y in [%g, %g] outside limits [%g, %g]" % (ax.get_title() or "axes", min(ys), max(ys), yl[0], yl[1]))
        from matplotlib.ticker import FixedLocator as _FL
        for name, axis, ticks in (("x", ax.xaxis, ax.get_xticks()), ("y", ax.yaxis, ax.get_yticks())):
            if isinstance(axis.get_major_locator(), _FL):
                continue                 # categorical axis (bar rows etc.): labels, not a scale
            t = sorted(v for v in ticks if (xl if name == "x" else yl)[0] <= v <= (xl if name == "x" else yl)[1])
            if len(t) >= 2:
                step = _np.abs(_np.diff(t))
                if not _np.allclose(step, step[0]): problems.append("%s: uneven %s ticks" % (ax.get_title() or "axes", name))
                mant = step[0] / 10 ** _np.floor(_np.log10(step[0]))
                if not any(_np.isclose(mant, m) for m in (1, 2, 2.5, 5)):
                    problems.append("%s: %s tick step %g is not 1/2/5x10^n" % (ax.get_title() or "axes", name, step[0]))
                report.append("%s %s-step %g" % (ax.get_title() or "axes", name, step[0]))
        mx, my = ax.margins()
        if mx > max_margin + 1e-9 or my > max_margin + 1e-9:
            problems.append("%s: margins %.2f/%.2f exceed %.2f" % (ax.get_title() or "axes", mx, my, max_margin))
    if problems:
        raise AssertionError("check_axes: " + "; ".join(problems))
    return report


def save(fig, path_png, provenance=None):
    """F9 output + F10 provenance sidecar. `provenance` = dict(experiment=, runs=[run ids],
    canon_version=, sources=[files]); the sidecar lets scripts/fig_stale_check.py flag the figure
    once any listed run is superseded or the Canon version moves."""
    import json as _json, io as _io, datetime as _dt
    check_axes(fig)
    fig.savefig(path_png, dpi=600, bbox_inches="tight", pad_inches=0.05)
    fig.savefig(path_png[:-4] + ".pdf", bbox_inches="tight", pad_inches=0.05)
    if provenance is not None:
        prov = dict(provenance); prov.setdefault("generated", _dt.datetime.now().isoformat(timespec="seconds"))
        prov["figure"] = path_png
        _io.open(path_png + ".provenance.json", "w", encoding="utf-8").write(_json.dumps(prov, ensure_ascii=False, indent=1))
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
