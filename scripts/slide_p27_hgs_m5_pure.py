# -*- coding: utf-8 -*-
"""Rebuild slide p27 as the PURE-GREEDY HGS-M5 flowchart (no price, no lambda loop) in native shapes.

Keeps the template's header/footer/logos, deletes the old body, and draws the Canon v2 inner loop
(draws first, dispatch only when no draw is negative, whole-order fallback when the split budget
binds). Same geometry as scripts/fig_m5_flowchart.py, so the PNG and the PPTX agree.

usage: python scripts/slide_p27_hgs_m5.py <template.pptx> <out.pptx>
"""
import sys, copy
from pptx import Presentation
from pptx.util import Emu, Pt
from pptx.enum.shapes import MSO_SHAPE, MSO_CONNECTOR
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.dml.color import RGBColor
from pptx.oxml.ns import qn
from lxml import etree

NAVY, NAVY_FILL = RGBColor(0x1F, 0x38, 0x64), RGBColor(0xDE, 0xEA, 0xF6)
ORANGE, ORANGE_FILL = RGBColor(0xC5, 0x5A, 0x11), RGBColor(0xFB, 0xE5, 0xD6)
GREY, GREY_FILL, BLACK, WHITE = RGBColor(0x7F, 0x7F, 0x7F), RGBColor(0xF2, 0xF2, 0xF2), RGBColor(0, 0, 0), RGBColor(0xFF, 0xFF, 0xFF)
FONT = "Times New Roman"

src, dst = sys.argv[1], sys.argv[2]
prs = Presentation(src)
slide = prs.slides[0]

# ---- keep header / footer / logos, drop the old body
KEEP = {"Group 2", "Group 4", "Group 6", "TextBox 213", "TextBox 214", "TextBox 215", "TextBox 216", "TextBox 217", "TextBox 218", "TextBox 219"}
for sh in list(slide.shapes):
    if sh.name not in KEEP:
        sh._element.getparent().remove(sh._element)
for sh in slide.shapes:
    if sh.name == "TextBox 214":
        r = sh.text_frame.paragraphs[0].runs
        r[0].text = "Proposed Heuristic Model : HGS-M5"
        for extra in r[1:]: extra.text = ""

# ---- coordinate mapping: figure units (0..133.3 x, 0..62 y, y up) -> EMU
S = 131000; OX = 350000; OY = 1200000
def X(u): return int(OX + u * S)
def Y(u): return int(OY + (62 - u) * S)
def W(u): return int(u * S)

def _style_text(tf, lines, size, color, bold, align=PP_ALIGN.CENTER):
    tf.word_wrap = True
    tf.margin_left = tf.margin_right = Emu(40000); tf.margin_top = tf.margin_bottom = Emu(20000)
    tf.vertical_anchor = MSO_ANCHOR.MIDDLE
    first = True
    for text, sz, col, b in lines:
        p = tf.paragraphs[0] if first else tf.add_paragraph()
        first = False
        p.alignment = align
        run = p.add_run(); run.text = text
        run.font.name = FONT; run.font.size = Pt(sz); run.font.bold = b; run.font.color.rgb = col

def box(x, y, w, h, title, sub=None, fill=NAVY_FILL, edge=NAVY, bold=True, fs=13, sfs=10.5, tcolor=BLACK, shape=MSO_SHAPE.ROUNDED_RECTANGLE):
    sh = slide.shapes.add_shape(shape, X(x - w / 2), Y(y + h / 2), W(w), W(h))
    sh.fill.solid(); sh.fill.fore_color.rgb = fill
    sh.line.color.rgb = edge; sh.line.width = Pt(1.25)
    sh.shadow.inherit = False
    if shape == MSO_SHAPE.ROUNDED_RECTANGLE:
        sh.adjustments[0] = 0.12
    lines = [(title, fs, tcolor, bold)] + ([(sub, sfs, tcolor, False)] if sub else [])
    _style_text(sh.text_frame, lines, fs, tcolor, bold)
    return sh

def diamond(x, y, w, h, text, fs=12):
    sh = slide.shapes.add_shape(MSO_SHAPE.DIAMOND, X(x - w / 2), Y(y + h / 2), W(w), W(h))
    sh.fill.solid(); sh.fill.fore_color.rgb = WHITE
    sh.line.color.rgb = ORANGE; sh.line.width = Pt(1.5); sh.shadow.inherit = False
    _style_text(sh.text_frame, [(text, fs, NAVY, True)], fs, NAVY, True)
    return sh

def label(x, y, text, color=ORANGE, fs=10.5, ha="center", bold=False, w=None):
    lines = text.split("\n")
    est_w = w if w else max(len(l) for l in lines) * fs * 0.55 / 72 * 914400 + 100000   # EMU estimate
    h = int(len(lines) * fs * 1.25 / 72 * 914400 + 60000)
    left = X(x) - (est_w // 2 if ha == "center" else (0 if ha == "left" else est_w))
    tb = slide.shapes.add_textbox(int(left), Y(y) - h // 2, int(est_w), h)
    tf = tb.text_frame; tf.word_wrap = False
    tf.margin_left = tf.margin_right = tf.margin_top = tf.margin_bottom = Emu(0)
    tf.vertical_anchor = MSO_ANCHOR.MIDDLE
    align = {"center": PP_ALIGN.CENTER, "left": PP_ALIGN.LEFT, "right": PP_ALIGN.RIGHT}[ha]
    _style_text(tf, [(l, fs, color, bold) for l in lines], fs, color, bold, align)
    return tb

def _arrowhead(conn):
    ln = conn.line._get_or_add_ln()
    tail = etree.SubElement(ln, qn("a:tailEnd")); tail.set("type", "triangle"); tail.set("w", "med"); tail.set("len", "med")

def seg(p, q, color=NAVY, head=False, lw=1.25):
    c = slide.shapes.add_connector(MSO_CONNECTOR.STRAIGHT, X(p[0]), Y(p[1]), X(q[0]), Y(q[1]))
    c.line.color.rgb = color; c.line.width = Pt(lw)
    if head: _arrowhead(c)
    return c

def elbow(pts, color=NAVY, head=True):
    for i in range(len(pts) - 1):
        seg(pts[i], pts[i + 1], color, head=(head and i == len(pts) - 2))

def arrow(p, q, color=NAVY): seg(p, q, color, head=True)

# ───────────────────────── column headers
label(3, 59.5, "One decision epoch  ·  BuildPlan", NAVY, 12, ha="left", bold=True)
label(88, 59.5, "Move value (line-equivalents)", GREY, 12, ha="left")

# ───────────────────────── main flow (single column, centred left)
IX = 40; IW = 52
box(IX, 55.0, IW, 3.6, "Copy the working books")
box(IX, 48.6, IW, 5.2, "①  Enumerate draw-line moves", "Station stock covers the line IN FULL  ·  slot available or already held")
diamond(IX, 41.4, 26, 5.0, "any draw available ?")
box(IX, 34.2, IW, 5.2, "②  Score every dispatch candidate", "Pod × station × nearest free robot  ·  value = lines it unlocks now", fill=ORANGE_FILL, edge=ORANGE)
diamond(IX, 27.0, 32, 5.0, "best dispatch unlocks ≥ 1 line ?", fs=11.5)
diamond(IX, 19.8, 28, 5.0, "split budget bound ?", fs=11.5)
box(IX, 12.6, IW, 5.2, "③  Whole-order fallback", "Cheapest pod set covering ONE unsplit order  ·  any cover accepted", fill=ORANGE_FILL, edge=ORANGE)
box(IX, 5.4, IW, 3.6, "commit plan  →  picking requests + robot tasks", fill=GREY_FILL, edge=GREY, bold=False)
arrow((IX, 53.2), (IX, 51.3)); arrow((IX, 46.0), (IX, 44.0))
arrow((IX, 38.9), (IX, 36.9)); label(IX + 0.9, 38.1, "no", GREY, ha="left")
arrow((IX, 31.6), (IX, 29.6))
arrow((IX, 24.5), (IX, 22.4)); label(IX + 0.9, 23.7, "no", GREY, ha="left")
arrow((IX, 17.3), (IX, 15.3)); label(IX + 0.9, 16.5, "yes", ORANGE, ha="left")
NX = IX + 31
elbow([(IX + 14, 19.8), (NX, 19.8), (NX, 5.4), (IX + IW / 2, 5.4)]); label(NX - 0.6, 12.6, "no", GREY, ha="right")
RX = IX + 28
elbow([(IX + 13, 41.4), (RX, 41.4), (RX, 48.6), (IX + IW / 2, 48.6)], ORANGE)
label(IX + 13.6, 42.6, "yes: take the most valuable draw", ORANGE, 10, ha="left")
elbow([(IX + 16, 27.0), (RX, 27.0), (RX, 41.4)], ORANGE, head=False)
label(IX + 16.6, 28.2, "yes: dispatch it", ORANGE, 10, ha="left")
elbow([(IX - IW / 2, 12.6), (IX - 30, 12.6), (IX - 30, 48.6), (IX - IW / 2, 48.6)], ORANGE)
label(IX - 29.4, 30.6, "bundle accepted", ORANGE, 10, ha="left")
label(RX - 0.6, 45.6, "books changed", GREY, 10, ha="right")
label(3, 1.2, "No outer loop: one pass per decision epoch.  Distance breaks ties only.", GREY, 10, ha="left")

# ───────────────────────── value cards (right column)
SX = 110; SW = 42
def card(yc, h, edge, title, rows, foot=None):
    box(SX, yc, SW, h, "", fill=WHITE, edge=edge)
    top = yc + h / 2 - 1.9
    label(SX - SW / 2 + 1.2, top, title, NAVY, 11.5, ha="left", bold=True)
    yy = top - 3.0
    for formula, note in rows:
        label(SX - SW / 2 + 1.2, yy, formula, NAVY, 12.5, ha="left")
        label(SX + SW / 2 - 1.2, yy, note, ORANGE, 10, ha="right")
        yy -= 2.7
    if foot: label(SX - SW / 2 + 1.2, yy + 0.4, foot, GREY, 10, ha="left")
card(50.0, 12.4, NAVY, "DRAW from pods already at the station",
     [("+1", "one order-line closed in full"), ("+κ", "last line of an order  (κ = lines per order)"), ("tie → keep a slot free", "")])
card(35.6, 12.4, ORANGE, "DISPATCH a robot to fetch a new pod",
     [("Σ (draws WITH pod − WITHOUT)", "lines it unlocks now"), ("β-weighted promises", "coverable lines still slot-blocked"), ("distance", "tie-break only")])
card(21.2, 12.4, ORANGE, "WHOLE-ORDER fallback (budget bound)",
     [("cheapest covering pod set", "one bot per pod"), ("accept any cover", "cheapest wins")],
     foot="next epoch re-enters ①")
prs.save(dst)
print("->", dst)
