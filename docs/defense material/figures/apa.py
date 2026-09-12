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
