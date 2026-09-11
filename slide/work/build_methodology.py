# -*- coding: utf-8 -*-
"""Chapter 05 Methodology — five slides, every constraint listed, all maths
rendered from LaTeX rather than hard-set as text sub/superscripts.

Design vocabulary lifted from the deck's own content slides (10-13):
  dark header bar  #1F3864 navy / #3B6135 green, white Times New Roman Bold
  light panel      #EDF3FC / #EEF5EA
  body             Times New Roman 15-18pt #404040
  accent           #4F8B3A
Canvas is 20" x 11.25"; the content band is x 0.385..19.615, y 1.45..10.10.

Every model statement here was read off the source, not recalled:
  M1G  shi2-shi13   RAWSimO.Core/.../M1GManager.cs:714-771, objective :694-713
  M4G  R/V/B, T1-T6 RAWSimO.Core/.../M4GManager.cs:722-1467, objective :1503-1672
  M5   move set     RAWSimO.Core/.../GreedyM5Manager.cs:13-50
Gated-off constraints (V7*, V8*, V9, B8*, B9, B10-B12, B1eq, B3s) are omitted:
they are inert under the canonical m4g.xconf.
"""
import io, math, os, re

import eqlib
from eqlib import emu

E = 914400.0
SLIDES = "unpacked/ppt/slides"

NAVY, NAVY_LT = "1F3864", "EDF3FC"
GREEN, GREEN_LT = "3B6135", "EEF5EA"
ACC = "4F8B3A"
BODY, MUTED, WHITE = "404040", "595959", "FFFFFF"
TNR, TNRB = "Times New Roman", "Times New Roman Bold"
EQ_INK, EQ_NAVY, EQ_GREEN, EQ_ACC = "#404040", "#1F3864", "#3B6135", "#4F8B3A"

L, R = 0.385, 19.615
TOP, BOT = 1.45, 10.10

_uid = [400]
_rels = [None]


def nid():
    _uid[0] += 1
    return _uid[0]


def esc(t):
    return t.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


# ── text ──────────────────────────────────────────────────────────────────
def run(text, sz=1500, color=BODY, bold=False, italic=False):
    face = TNRB if bold else TNR
    a = 'lang="en-US" sz="%d"' % sz
    if bold:
        a += ' b="true"'
    if italic:
        a += ' i="true"'
    return ('<a:r><a:rPr %s><a:solidFill><a:srgbClr val="%s"/></a:solidFill>'
            '<a:latin typeface="%s"/><a:ea typeface="%s"/><a:cs typeface="%s"/>'
            '</a:rPr><a:t xml:space="preserve">%s</a:t></a:r>'
            % (a, color, face, face, face, esc(text)))


def para(runs, line=None, before=0, align="l"):
    p = '<a:pPr algn="%s">' % align
    if line:
        p += '<a:lnSpc><a:spcPts val="%d"/></a:lnSpc>' % line
    if before:
        p += '<a:spcBef><a:spcPts val="%d"/></a:spcBef>' % before
    p += "<a:buNone/></a:pPr>"
    return "<a:p>" + p + "".join(runs) + "</a:p>"


def text_h(spec, width):
    """Estimated rendered height in inches. spec: [(chars, sz, lnSpc, before)]."""
    total = 0.0
    for chars, sz, lnspc, before in spec:
        per_line = width / (0.48 * sz / 7200.0)
        lines = max(1, int(math.ceil(chars / per_line - 1e-6)))
        total += lines * lnspc / 7200.0 + before / 7200.0
    return total


def tbox(x, y, w, h, paras):
    i = nid()
    return ('<p:sp><p:nvSpPr><p:cNvPr name="Text %d" id="%d"/><p:cNvSpPr txBox="true"/>'
            '<p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="%d" y="%d"/>'
            '<a:ext cx="%d" cy="%d"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/>'
            '</a:prstGeom><a:noFill/></p:spPr><p:txBody>'
            '<a:bodyPr anchor="t" wrap="square" rtlCol="false" tIns="0" lIns="0" '
            'bIns="0" rIns="0"/><a:lstStyle/>%s</p:txBody></p:sp>'
            % (i, i, emu(x), emu(y), emu(w), emu(h), "".join(paras)))


def rect(x, y, w, h, fill):
    i = nid()
    return ('<p:sp><p:nvSpPr><p:cNvPr name="Shape %d" id="%d"/><p:cNvSpPr/><p:nvPr/>'
            '</p:nvSpPr><p:spPr><a:xfrm><a:off x="%d" y="%d"/><a:ext cx="%d" cy="%d"/>'
            '</a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom>'
            '<a:solidFill><a:srgbClr val="%s"/></a:solidFill><a:ln><a:noFill/></a:ln>'
            '</p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p>'
            '<a:endParaRPr lang="en-US"/></a:p></p:txBody></p:sp>'
            % (i, i, emu(x), emu(y), emu(w), emu(h), fill))


# ── maths ─────────────────────────────────────────────────────────────────
def eq(latex, x, y, fs=17, color=EQ_INK):
    """Place one LaTeX expression at its natural size. Returns (xml, w, h)."""
    name, w, h = eqlib.render(latex, fs, color)
    rid = _rels[0].image(name)
    return eqlib.pic(nid(), rid, x, y, w, h), w, h


# ── blocks ────────────────────────────────────────────────────────────────
BAR_H = 0.46


def bar(x, y, w, label, dark=NAVY):
    return (rect(x, y, w, BAR_H, dark)
            + tbox(x + 0.20, y + 0.11, w - 0.40, BAR_H - 0.18,
                   [para([run(label, 1370, WHITE, bold=True)], line=1580)]))


def block(x, y, w, h, label, dark=NAVY, light=NAVY_LT):
    """Header bar plus an empty light panel. Returns (xml, inner box)."""
    xml = bar(x, y, w, label, dark) + rect(x, y + BAR_H, w, h - BAR_H, light)
    return xml, (x + 0.24, y + BAR_H + 0.16, w - 0.48, h - BAR_H - 0.28)


def cons_grid(x, y, w, h, items, cols=2, fs=17, gap=0.30, note_sz=1300,
              row_gap=0.19):
    """Constraint rows (tag, latex, note) in sub-columns, each stacked under the
    previous at its own measured height."""
    cw = (w - gap * (cols - 1)) / cols
    per = int(math.ceil(len(items) / float(cols)))
    xml = ""
    for c in range(cols):
        cx = x + c * (cw + gap)
        cy = y
        for tag, latex, note in items[c * per:(c + 1) * per]:
            tw = 0.86 if tag else 0.0
            e, ew, eh = eq(latex, cx + tw, cy, fs)
            xml += e
            if tag:
                xml += tbox(cx, cy + max(0.0, (eh - 0.24) / 2.0), tw - 0.06, 0.26,
                            [para([run(tag, 1300, NAVY, bold=True)], line=1520)])
            row = max(eh, 0.26)
            if note:
                nh = text_h([(len(note), note_sz, int(note_sz * 1.22), 0)], cw - tw)
                xml += tbox(cx + tw, cy + row + 0.03, cw - tw, nh + 0.04,
                            [para([run(note, note_sz, MUTED)],
                                  line=int(note_sz * 1.22))])
                row += 0.03 + nh
            cy += row + row_gap
    return xml


# ══════════════════════════════════════════════════════════════════════════
# 1 — Baseline: M1G
# ══════════════════════════════════════════════════════════════════════════
def s_m1g():
    """Baseline M1G in the notation of Jiao et al. (2026), Eqs (2)-(16) and (19).
    The symbol map to the implementation is stated on the slide itself."""
    xml = ""
    lw = 8.55
    rw = R - L - lw - 0.38
    rx = L + lw + 0.38

    # ── sets and parameters ────────────────────────────────────────────────
    sp_h = 3.52
    b, (ix, iy, iw, ih) = block(L, TOP, lw, sp_h, "Sets and Parameters")
    xml += b
    cy = iy
    for latex, note in [
        (r"O(t),\; \hat{O}(t)",
         "pending orders; those whose residence time exceeds tau"),
        (r"W_a(t)", "workstations with free capacity"),
        (r"I", "SKUs"),
        (r"P,\; P_a(t),\; P_w(t)",
         "all pods; idle pods; pods already assigned to w"),
        (r"R,\; R_a(t),\; R_b(t)", "all robots; idle robots; busy robots"),
        (r"\tilde{O}_i(t),\; \tilde{P}_i(t)",
         "orders demanding SKU i; pods holding SKU i"),
        (r"n_{i,o},\; \hat{n}_{i,p}(t)",
         "units of SKU i in order o; units of SKU i on pod p"),
        (r"c_w(t),\; d_{p,w},\; d_{r,p}",
         "free capacity of w, counted in ORDERS; pod-station and robot-pod distance"),
    ]:
        e, ew, eh = eq(latex, ix, cy, 15)
        xml += e
        nh = text_h([(len(note), 1350, 1640, 0)], iw - 2.55)
        xml += tbox(ix + 2.55, cy + max(0.0, (eh - nh) / 2.0), iw - 2.55, nh + 0.05,
                    [para([run(note, 1350, BODY)], line=1640)])
        cy += max(eh, nh, 0.24) + 0.10

    # ── decision variables ─────────────────────────────────────────────────
    v_y = TOP + sp_h + 0.16
    b, (ix, iy, iw, ih) = block(L, v_y, lw, BOT - v_y, "Decision Variables")
    xml += b
    cy = iy
    for latex, note, ec, tc in [
        (r"\hat{x}_{o,w}", "order o COULD be assigned to w — not slot-limited",
         EQ_NAVY, NAVY),
        (r"x_{o,w}", "order o IS assigned to w — consumes a slot", EQ_GREEN, GREEN),
        (r"z_{p,w}", "pod p is assigned to workstation w", EQ_INK, BODY),
        (r"y_{r,p}", "robot r transports pod p — task allocation is inside the model",
         EQ_INK, BODY),
        (r"\delta_{o,p,w}", "auxiliary: pod p supplies order o at w", EQ_INK, BODY),
        (r"u_w \in \mathbb{Z}^{+}_{0}", "unused capacity of workstation w",
         EQ_INK, BODY),
    ]:
        e, ew, eh = eq(latex, ix, cy, 17, ec)
        xml += e
        nh = text_h([(len(note), 1400, 1700, 0)], iw - 1.95)
        xml += tbox(ix + 1.95, cy + max(0.0, (eh - nh) / 2.0), iw - 1.95, nh + 0.05,
                    [para([run(note, 1400, tc)], line=1700)])
        cy += max(eh, nh, 0.26) + 0.10
    _m = ("All binary except u.  Implementation names: x-hat, x, z, y, delta, u  ->  "
          "yos, yaos, xps, yrp, dops, us.  The delta here is Jiao's auxiliary "
          "variable and is unrelated to the realisation rate used from the next "
          "slide onward.")
    _mh = text_h([(len(_m), 1250, 1520, 0)], iw)
    xml += tbox(ix, cy + 0.06, iw, _mh + 0.05,
                [para([run(_m, 1250, MUTED)], line=1520)])
    cy += 0.06 + _mh + 0.18
    xml += rect(ix, cy, iw, 0.022, NAVY)
    cy += 0.16
    _w = ("Where the implementation departs from the paper: u is capped at 6 rather "
          "than left unbounded; the (19) substitution fires only when the urgent set "
          "exceeds total free capacity, not whenever it is non-empty; and orders "
          "whose system stock cannot cover them in full are filtered out before the "
          "solve, a step the paper does not state.")
    _wh = text_h([(len(_w), 1250, 1520, 0)], iw)
    xml += tbox(ix, cy, iw, _wh + 0.05,
                [para([run(_w, 1250, MUTED)], line=1520)])

    # ── objective and preprocessing ────────────────────────────────────────
    o_h = 2.55
    b, (ix, iy, iw, ih) = block(rx, TOP, rw, o_h,
                                "Objective  (14)  and Preprocessing  (19)")
    xml += b
    e, ew, eh = eq(r"\min\;\; \alpha_1 \!\!\sum_{o \in O(t)}\sum_{w \in W_a(t)}\!\!"
                   r"\hat{x}_{o,w} \;+\; \alpha_2 \!\left("
                   r"\sum_{p \in P_a(t)}\sum_{w \in W_a(t)}\!\! d_{p,w}\, z_{p,w}"
                   r"\; + \!\!\sum_{r \in R_a(t)}\sum_{p \in P_a(t)}\!\! d_{r,p}\, y_{r,p}"
                   r"\right) \;+\; \alpha_3 \!\!\sum_{w \in W_a(t)}\!\! u_w",
                   ix, iy, 17)
    xml += e
    cy = iy + eh + 0.12
    e2, _, eh2 = eq(r"\alpha_1 = -40 \qquad \alpha_2 = 1 \;\;(\mathrm{metres})"
                    r" \qquad \alpha_3 = 1000", ix, cy, 16, "#595959")
    xml += e2
    cy += eh2 + 0.18
    e3, _, eh3 = eq(r"O(t) \leftarrow \hat{O}(t), \quad \mathrm{if}\;\;"
                    r" | \hat{O}(t) | > 0", ix, cy, 16)
    xml += e3
    _p = ("Preprocessing (19): orders that have waited longer than tau take "
          "priority, so long-waiting orders are not starved by the objective.")
    ph = text_h([(len(_p), 1300, 1580, 0)], iw - 3.20)
    xml += tbox(ix + 3.20, cy + max(0.0, (eh3 - ph) / 2.0), iw - 3.20, ph + 0.05,
                [para([run(_p, 1300, MUTED)], line=1580)])

    # ── constraints ────────────────────────────────────────────────────────
    c_y = TOP + o_h + 0.16
    b, (ix, iy, iw, ih) = block(rx, c_y, rw, BOT - c_y, "Constraints  (2) – (16)")
    xml += b
    items = [
        ("(2)", r"\sum_{w \in W_a(t)} \hat{x}_{o,w} \leq 1 \quad \forall o \in O(t)",
         "one order to at most one workstation"),
        ("(3)", r"x_{o,w} \leq \hat{x}_{o,w} \quad \forall o,\, w",
         "an assignable order is not necessarily assigned"),
        ("(4)", r"\sum_{o \in O(t)} x_{o,w} = c_w(t) - u_w \quad \forall w",
         "assigned orders equal capacity minus unused capacity"),
        ("(5)", r"\sum_{o \in \tilde{O}_i(t)} n_{i,o}\, \hat{x}_{o,w} \leq"
                r" \sum_{p \in \tilde{P}_i(t)} \hat{n}_{i,p}(t)\, z_{p,w}"
                r" \quad \forall i,\, w",
         "demand for SKU i at w cannot exceed what the pods at w supply"),
        ("(6)", r"\sum_{w \in W_a(t)} z_{p,w} \leq 1 \quad \forall p \in P",
         "a pod goes to at most one workstation"),
        ("(7)", r"z_{p,w} = 1 \quad \forall w,\; p \in P_w(t)",
         "inherit pods already assigned to w"),
        ("(8)", r"\sum_{w \in W_a(t)} z_{p,w} \leq \sum_{r \in R} y_{r,p}"
                r" \quad \forall p \in P", "dispatching a pod requires a robot"),
        ("(9)", r"\sum_{r \in R} y_{r,p} \leq 1 \quad \forall p \in P",
         "a pod is transported by at most one robot"),
        ("(10)", r"\sum_{p \in P} y_{r,p} \leq 1 \quad \forall r \in R",
         "a robot transports at most one pod"),
        ("(11)", r"y_{r,p_r} = 1 \quad \forall r \in R_b(t)",
         "a busy robot continues its current transport task"),
        ("(12)", r"2\, \delta_{o,p,w} \leq x_{o,w} + z_{p,w}"
                 r" \quad \forall w,\, p \in P_a(t),\, o",
         "supply requires the order assigned AND the pod assigned"),
        ("(13)", r"z_{p,w} \leq \sum_{o \in O(t)} \delta_{o,p,w}"
                 r" \quad \forall w,\, p \in P_a(t)",
         "a newly assigned pod must supply at least one assigned order"),
    ]
    xml += cons_grid(ix, iy, iw, ih, items, cols=2, fs=15, note_sz=1200,
                     row_gap=0.15)
    e4, _, eh4 = eq(r"(15)\;\; \hat{x}_{o,w},\, x_{o,w},\, z_{p,w},\,"
                    r" \delta_{o,p,w},\, y_{r,p} \in \{0,1\}"
                    r" \qquad (16)\;\; u_w \in \mathbb{Z}^{+}_{0}",
                    ix, iy + ih - 0.30, 14)
    xml += e4
    return xml


# ══════════════════════════════════════════════════════════════════════════
# 2 — Key Observation
# ══════════════════════════════════════════════════════════════════════════
def s_twolayer():
    xml = ""
    cw = (R - L - 0.45) / 2.0
    rx = L + cw + 0.45

    b, (ix, iy, iw, ih) = block(L, TOP, cw, BOT - TOP,
                                "The Two Layers Already in M1G")
    xml += b
    xml += tbox(ix, iy, iw, 0.72, [para([
        run("M1G already separates a ", 1700, BODY),
        run("valuation", 1700, NAVY, bold=True), run(" layer from a ", 1700, BODY),
        run("binding", 1700, GREEN, bold=True),
        run(" layer. This study does not invent that split — it re-prices it.",
            1700, BODY)], line=2080)])
    cy = iy + 0.92
    for latex, colr, title, tcol, body in [
        (r"y_{os}", EQ_NAVY, "valuation", NAVY,
         "Constrained by shi2 and shi5 only. Free of the slot limit, so it may "
         "claim more value than the station can hold. Carries w2 = -40."),
        (r"y_{aos}", EQ_GREEN, "binding", GREEN,
         "Constrained by shi3 and shi4. This is what actually executes — the "
         "station's order queue is built from it. Carries no coefficient of its own."),
    ]:
        e, ew, eh = eq(latex, ix, cy, 22, colr)
        xml += e
        xml += tbox(ix + 1.10, cy + 0.04, iw - 1.10, 0.36,
                    [para([run(title, 1800, tcol, bold=True)], line=2120)])
        nh = text_h([(len(body), 1500, 1880, 0)], iw - 1.10)
        xml += tbox(ix + 1.10, cy + 0.48, iw - 1.10, nh + 0.06,
                    [para([run(body, 1500, BODY)], line=1880)])
        cy += 0.48 + nh + 0.44

    xml += tbox(ix, cy + 0.18, iw, 0.40,
                [para([run("Substituting the shi4 equality", 1700, BODY, bold=True)],
                      line=2040)])
    cy += 0.70
    for latex in [r"u_s = C_s - \sum_{o \in O} y_{aos}",
                  r"w_3 \sum_{s \in S} u_s \;=\; w_3 \sum_{s \in S} C_s"
                  r"\;-\; 1000 \sum_{o \in O}\sum_{s \in S} y_{aos}"]:
        e, ew, eh = eq(latex, ix, cy, 19)
        xml += e
        cy += eh + 0.24

    b, (jx, jy, jw, jh) = block(rx, TOP, cw, BOT - TOP,
                                "Consequence  ·  An Implicit Realisation Rate",
                                GREEN, GREEN_LT)
    xml += b
    xml += tbox(jx, jy, jw, 0.48, [para([run(
        "The constant drops out of the argmin, so what the objective really pays is:",
        1550, BODY)], line=1900)])
    cy = jy + 0.64
    for latex, colr in [
        (r"\mathrm{valuation}\;\; y_{os} \;\longrightarrow\; -40", EQ_NAVY),
        (r"\mathrm{binding}\;\; y_{aos} \;\longrightarrow\; -1000", EQ_GREEN),
    ]:
        e, ew, eh = eq(latex, jx, cy, 19, colr)
        xml += e
        cy += eh + 0.18
    cy += 0.26
    e, ew, eh = eq(r"\delta \;=\; \frac{|w_2|}{w_3} \;=\; \frac{40}{1000}"
                   r"\;\approx\; 0.038", jx, cy, 26, EQ_ACC)
    xml += e
    cy += eh + 0.34
    xml += tbox(jx, cy, jw, 1.20, [
        para([run("The ratio between the two layers is pinned by hand.",
                  1600, BODY, bold=True)], line=1960),
        para([run("The long-term benefit it stands for is, in the authors' own "
                  "words, “cannot be accurately quantified”.",
                  1500, MUTED)], line=1860, before=140)])
    cy += 1.34
    xml += rect(jx, cy, jw, 0.028, GREEN)
    cy += 0.26
    xml += tbox(jx, cy, jw, max(0.6, BOT - 0.28 - cy), [
        para([run("What this study changes", 1700, GREEN, bold=True)], line=2040),
        para([run("Reducing the decision atom from the order to the item makes "
                  "this ratio instance-dependent. It is therefore replaced by a "
                  "quantity measured from the run in progress — the share of "
                  "valued completions the binding layer actually takes — "
                  "rather than a constant chosen in advance.", 1500, BODY)],
             line=1900, before=170)])
    return xml


# ══════════════════════════════════════════════════════════════════════════
# 3 — Proposed Exact Model: M4G
# ══════════════════════════════════════════════════════════════════════════
def s_m4g():
    xml = ""
    lw = 8.10
    rw = R - L - lw - 0.40
    rx = L + lw + 0.40

    v_h = 2.90
    b, (ix, iy, iw, ih) = block(L, TOP, lw, v_h, "Decision Variables")
    xml += b
    cy = iy
    for latex, note, ec, tc in [
        (r"\hat{q}_{oips} \in \mathbb{Z}_{\geq 0}",
         "VALUATION draw: units of SKU i of order o from pod p at station s",
         EQ_NAVY, NAVY),
        (r"\hat{c}_{oi},\; \hat{z}_{o} \in \{0,1\}",
         "valued line closure and valued order completion", EQ_NAVY, NAVY),
        (r"q_{oips} \in \mathbb{Z}_{\geq 0}",
         "BINDING draw — what is actually executed", EQ_GREEN, GREEN),
        (r"y_{os},\; c_{oi},\; z_{o} \in \{0,1\}",
         "slot occupancy, bound line closure, bound completion", EQ_GREEN, GREEN),
        (r"x_{ps},\; y_{rp} \in \{0,1\}",
         "pod and robot assignment — unchanged from M1G", EQ_INK, BODY),
    ]:
        e, ew, eh = eq(latex, ix, cy, 17, ec)
        xml += e
        nh = text_h([(len(note), 1400, 1680, 0)], iw - 2.90)
        xml += tbox(ix + 2.90, cy + max(0.0, (eh - nh) / 2.0), iw - 2.90, nh + 0.05,
                    [para([run(note, 1400, tc)], line=1680)])
        cy += max(eh, nh, 0.26) + 0.11

    o_y = TOP + v_h + 0.18
    b, (ix, iy, iw, ih) = block(L, o_y, lw, BOT - o_y, "Objective  ·  T1–T6",
                                GREEN, GREEN_LT)
    xml += b
    e, ew, eh = eq(r"\min\;\; D / V \qquad\Longrightarrow\qquad \min\;\; D - \lambda V",
                   ix, iy, 21, EQ_GREEN)
    xml += e
    cy = iy + eh + 0.16
    for tag, latex, note in [
        ("T1, T2", r"\sum_{p \in P_a}\sum_{s} c^{\,ps} x_{ps}"
                   r" + \sum_{r}\sum_{p \in P_a} c^{\,rp} y_{rp}",
         "travel: only newly dispatched pods pay; committed pods are sunk"),
        ("T3", r"-\lambda(1-\delta)\sum_{o,i} c_{oi}"
               r" \;-\; \lambda\delta \sum_{o,i} \hat{c}_{oi}",
         "line closure, split across the binding and valuation layers by delta"),
        ("T4", r"-\mu(1-\delta)\sum_{o} z_{o}"
               r" \;-\; \mu\delta \sum_{o} \hat{z}_{o}",
         "order completion, same split"),
        ("T5", r"-\varepsilon \sum_{o,i,p,s} q_{oips}",
         "immediacy: among equally valued solutions, prefer executing now"),
        ("T6", r"+\rho \!\!\sum_{p \in P_a}\!\! q_{oips}"
               r" \;-\; \rho \!\!\sum_{p \in P_p}\!\! q_{oips}",
         "pod tier, binding layer only — no counterpart under an order atom"),
    ]:
        e, ew, eh = eq(latex, ix + 0.94, cy, 14)
        xml += e
        xml += tbox(ix, cy + max(0.0, (eh - 0.20) / 2.0), 0.90, 0.26,
                    [para([run(tag, 1250, GREEN, bold=True)], line=1460)])
        nh = text_h([(len(note), 1200, 1470, 0)], iw - 0.94)
        xml += tbox(ix + 0.94, cy + max(eh, 0.22) + 0.02, iw - 0.94, nh + 0.05,
                    [para([run(note, 1200, MUTED)], line=1470)])
        cy += max(eh, 0.22) + 0.02 + nh + 0.10
    _c = ("Linearised by Dinkelbach. All prices are in metres, read off the run "
          "in progress: mu = distance per completed order, delta = bound over "
          "valued completions.")
    _ch = text_h([(len(_c), 1300, 1600, 0)], iw)
    xml += tbox(ix, cy + 0.06, iw, _ch + 0.06,
                [para([run(_c, 1300, BODY)], line=1600)])

    b, (ix, iy, iw, ih) = block(rx, TOP, rw, BOT - TOP,
                                "Constraints  ·  Resource, Valuation, Binding")
    xml += b
    items = [
        ("R1", r"\sum_{s} x_{ps} \leq 1 \quad \forall p", "a pod goes to one station"),
        ("R2", r"\sum_{s} x_{ps} \leq \sum_{r} y_{rp} \quad \forall p",
         "dispatching a pod requires a robot"),
        ("R3", r"\sum_{p} y_{rp} \leq 1 \quad \forall r", "one pod per robot"),
        ("R4", r"\sum_{r} y_{rp} \leq 1 \quad \forall p", "one robot per pod"),
        ("R5", r"x_{ps} = 1,\;\; y_{rp} = 1 \quad \forall p \in P_b",
         "inherit committed pods and robots"),
        ("V1", r"\sum_{o} \hat{q}_{oips} \leq k_{pi}\, x_{ps} \quad \forall i,p,s",
         "a draw needs stock on that pod and that pod dispatched"),
        ("V2", r"\sum_{p,s} \hat{q}_{oips} \leq d_{oi} \quad \forall o,i",
         "never draw more than the residual demand"),
        ("V2a", r"\sum_{o,s}\sum_{p \in P_a} \hat{q}_{oips}"
                r" \leq \max\left(0,\; D_i - S_i\right) \quad \forall i",
         "new pods are valued only against demand inbound supply cannot cover"),
        ("V3", r"\sum_{p,s} \hat{q}_{oips} \geq d_{oi}\, \hat{c}_{oi}"
               r" \quad \forall o,i", "a line counts as closed only when drawn in full"),
        ("V4", r"\hat{c}_{oi} \geq \hat{z}_{o} \quad \forall o,i",
         "completing an order requires every line closed"),
        ("V4g", r"\hat{z}_{o} = 0 \quad \forall o \in O^{\,unc}",
         "an order no pod can cover cannot collect the completion reward"),
        ("B1", r"q_{oips} \leq \hat{q}_{oips} \quad \forall o,i,p,s",
         "binding is a subset of valuation, per unit"),
        ("B2", r"\sum_{i,p} q_{oips} \leq D_{o}\, y_{os} \quad \forall o,s",
         "drawing for an order at a station occupies one of its slots"),
        ("B3", r"\sum_{o} y_{os} \leq C_{s} \quad \forall s",
         "slot capacity — an INEQUALITY, unlike shi4"),
        ("B4", r"y_{os} \leq \sum_{i,p} q_{oips} \quad \forall o,s",
         "no slot may be taken without a draw"),
        ("B5", r"\sum_{p,s} q_{oips} \geq d_{oi}\, c_{oi} \quad \forall o,i",
         "a bound closure needs the full residual bound"),
        ("B6", r"c_{oi} \leq \hat{c}_{oi}, \;\; z_{o} \leq \hat{z}_{o}",
         "bound closures and completions are subsets of valued ones"),
        ("B7", r"c_{oi} \geq z_{o} \quad \forall o,i",
         "a bound completion needs every line bound-closed"),
    ]
    xml += cons_grid(ix, iy, iw, ih, items, cols=2, fs=15)
    return xml


# ══════════════════════════════════════════════════════════════════════════
# 4 — Proposed Heuristic: M5
# ══════════════════════════════════════════════════════════════════════════
def s_m5():
    xml = ""
    lw = 10.30
    rw = R - L - lw - 0.45
    rx = L + lw + 0.45

    b, (ix, iy, iw, ih) = block(L, TOP, lw, BOT - TOP, "Marginal-Line Greedy")
    xml += b
    xml += tbox(ix, iy, iw, 0.76, [para([run(
        "M4G solves a MILP every time a slot frees. M5 solves the same model "
        "greedily: it repeatedly asks what the single best marginal move is, and "
        "takes it.", 1600, BODY)], line=1980)])
    cy = iy + 0.96
    xml += tbox(ix, cy, iw, 0.62, [para([
        run("A.  draw-line", 1650, NAVY, bold=True),
        run("      take one order line to its full current residual from stock "
            "already standing at a station", 1550, BODY)], line=1980)])
    cy += 0.72
    e, ew, eh = eq(r"\Delta \;=\; -\lambda \;\; [\,-\mu \;\; \mathrm{if\ last\ "
                   r"open\ line}\,] \;\pm\; \rho\, q \;-\; \varepsilon\, q",
                   ix + 0.30, cy, 18)
    xml += e
    cy += eh + 0.30
    xml += tbox(ix, cy, iw, 0.62, [para([
        run("B.  dispatch", 1650, NAVY, bold=True),
        run("        fetch an idle pod, paying robot-to-pod plus pod-to-station "
            "distance", 1550, BODY)], line=1980)])
    cy += 0.72
    e, ew, eh = eq(r"\Delta \;=\; c^{\,rp} + c^{\,ps} \;+\;"
                   r" \sum \Delta_{\mathrm{draw}} \;\; \mathrm{unlocked}",
                   ix + 0.30, cy, 18)
    xml += e
    cy += eh + 0.10
    xml += tbox(ix + 0.30, cy, iw - 0.30, 0.36, [para([run(
        "scored NET of the draws it unlocks — a dispatch alone is pure cost "
        "and would never be taken on its own", 1350, MUTED)], line=1620)])
    cy += 0.54
    xml += rect(ix, cy, iw, 0.028, NAVY)
    cy += 0.26
    xml += tbox(ix, cy, iw, 0.42, [para([run(
        "Accept the most negative move; stop when none is negative.",
        1750, NAVY, bold=True)], line=2100)])
    cy += 0.60
    xml += tbox(ix, cy, iw, BOT - 0.30 - cy, [para([
        run("Station slots and free robots remain ", 1550, BODY),
        run("hard constraints, never prices", 1550, BODY, bold=True),
        run(" — exactly as in M4G's B3. The splitting atom here is the LINE, "
            "not the unit: lambda accrues only when a line closes in full (V3, B5), "
            "so a per-unit move would score almost nothing until the last unit and "
            "a greedy search would never reach it.", 1550, BODY)], line=1900)])

    b, (jx, jy, jw, jh) = block(rx, TOP, rw, BOT - TOP,
                                "Why the Comparison Stays Clean", GREEN, GREEN_LT)
    xml += b
    xml += tbox(jx, jy, jw, 1.00, [para([run(
        "Every move sets a variable that already exists in M4G's MILP, and the "
        "loop keeps R1–R5, V1–V4 and B1–B7 satisfied throughout.",
        1550, BODY)], line=1900)])
    cy = jy + 1.14
    e, ew, eh = eq(r"\mathrm{obj}(\mathrm{M5}) \;\geq\; \mathrm{obj}(\mathrm{M4G})",
                   jx, cy, 21, EQ_GREEN)
    xml += e
    cy += eh + 0.24
    xml += tbox(jx, cy, jw, 1.10, [para([run(
        "Every M5 solution is feasible for M4G. With both objectives written from "
        "the same price list, the gap between them is a real and reportable "
        "optimality gap rather than an unexplained difference.", 1500, BODY)],
        line=1880)])
    cy += 1.26
    xml += rect(jx, cy, jw, 0.028, GREEN)
    cy += 0.26
    xml += tbox(jx, cy, jw, 0.40, [para([run(
        "The objective is M4G's, term for term", 1600, GREEN, bold=True)],
        line=1960)])
    cy += 0.52
    e, ew, eh = eq(r"-\lambda \;\; \mathrm{bound} \qquad -\lambda\delta \;\;"
                   r" \mathrm{valued\ only}", jx, cy, 17)
    xml += e
    cy += eh + 0.18
    xml += tbox(jx, cy, jw, max(0.6, BOT - 0.28 - cy), [para([run(
        "delta is calibrated from the same bound-to-valued ratio M4G feeds, so the "
        "two solvers price identically. The one declared difference is line "
        "granularity: M5 cannot assemble a single line from two pods, which M4G "
        "can. That gap is declared, not hidden.", 1500, BODY)], line=1880)])
    return xml


# ══════════════════════════════════════════════════════════════════════════
# 5 — Model Comparison Framework
# ══════════════════════════════════════════════════════════════════════════
def s_compare():
    xml = ""
    tw = R - L
    c0, c1 = 3.15, 7.60
    c2 = tw - c0 - c1
    hh, rh = 0.48, 0.56

    def cell(x, y, w, h, runs_):
        return tbox(x + 0.18, y + 0.14, w - 0.36, h - 0.20, [para(runs_, line=1740)])

    y = TOP
    xml += rect(L, y, tw, hh, NAVY)
    for cx, cw_, lab in [(L, c0, "Dimension"), (L + c0, c1, "M1G  (baseline)"),
                         (L + c0 + c1, c2, "M4G / M5  (proposed)")]:
        xml += tbox(cx + 0.18, y + 0.13, cw_ - 0.36, hh - 0.20,
                    [para([run(lab, 1370, WHITE, bold=True)], line=1580)])
    y += hh
    for label, a, bb, hi in [
        ("Decision atom", "order", "item", True),
        ("Order splitting", "none; one order in full at one station",
         "across stations, epochs and pods; exercised only when the prices justify it",
         False),
        ("Completion variable", "none; assignment implies coverage (shi5)",
         "required (c, z); partial fulfilment breaks the implication", False),
        ("Slot constraint", "shi4 equality; idle slots are force-filled",
         "B3 inequality; idle slots may be left open", False),
        ("Two-layer ratio", "pinned by hand, 1000 : 40",
         "measured from the run in progress", True),
        ("Objective form", "weighted linear sum, weights exogenous",
         "ratio D / V, linearised by Dinkelbach", False),
        ("Price dimension", "dimensionless weights", "all prices in metres", False),
    ]:
        xml += rect(L, y, tw, rh, GREEN_LT if hi else NAVY_LT)
        xml += cell(L, y, c0, rh, [run(label, 1450, NAVY, bold=True)])
        xml += cell(L + c0, y, c1, rh, [run(a, 1450, BODY)])
        xml += cell(L + c0 + c1, y, c2, rh,
                    [run(bb, 1450, ACC if hi else BODY, bold=hi)])
        y += rh
    xml += rect(L, y, tw, 0.66, "F2F2F2")
    xml += cell(L, y, c0, 0.66, [run("Held constant", 1450, MUTED, bold=True)])
    xml += tbox(L + c0 + 0.18, y + 0.14, tw - c0 - 0.36, 0.54, [para([run(
        "task allocation inside the model  ·  pod and robot constraints  "
        "·  decision triggered on every freed slot  ·  order-level "
        "station capacity  ·  identical path planning and pod-return policy",
        1450, BODY)], line=1740)])
    y += 0.66 + 0.26

    cw = (R - L - 0.45) / 2.0
    rx = L + cw + 0.45
    b, (ix, iy, iw, ih) = block(L, y, cw, BOT - y, "Evaluation Design")
    xml += b
    e, ew, eh = eq(r"y_{os} \in \{0,1\} \qquad\longrightarrow\qquad"
                   r" q_{oips} \in \mathbb{Z}_{\geq 0}", ix, iy, 18)
    xml += e
    cy = iy + eh + 0.20
    xml += tbox(ix, cy, iw, max(0.6, BOT - 0.28 - cy), [
        para([run("Small instance   ", 1500, NAVY, bold=True),
              run("M4G vs M1G", 1600, BODY, bold=True),
              run("      the exact model against the exact baseline it derives "
                  "from, under one simulator configuration.", 1450, BODY)],
             line=1820),
        para([run("Large instance   ", 1500, NAVY, bold=True),
              run("M5 vs HADGS", 1600, BODY, bold=True),
              run("      at fleet scale the baseline's own authors report M1G "
                  "still leaves a 15.4 % gap and evaluate only their heuristic, so "
                  "this follows their experimental design.", 1450, BODY)],
             line=1820, before=170)])

    b, (jx, jy, jw, jh) = block(rx, y, cw, BOT - y, "Experimental Controls",
                                GREEN, GREEN_LT)
    xml += b
    xml += tbox(jx, jy, jw, jh, [
        para([run("Configurations differ only in the order-batching block; every "
                  "arm runs on the same build.", 1450, BODY)], line=1820),
        para([run("Orders completed, items handled and backlog are reported "
                  "together: splitting creates work in progress, so completed "
                  "orders alone understate a splitting model.", 1450, BODY)],
             line=1820, before=150),
        para([run("Arms share seeds and are compared with a paired test.",
                  1450, BODY)], line=1820, before=150)])
    return xml


# ══════════════════════════════════════════════════════════════════════════
BUILDERS = [
    ("Baseline: M1G", s_m1g),
    ("Key Observation: The Two Layers Inside M1G", s_twolayer),
    ("Proposed Exact Model: M4G", s_m4g),
    ("Proposed Heuristic: M5", s_m5),
    ("Model Comparison Framework", s_compare),
]


def order_to_files():
    """Presentation order -> slideN.xml, via <p:sldIdLst> and the presentation rels."""
    pres = io.open("unpacked/ppt/presentation.xml", encoding="utf-8").read()
    rels = io.open("unpacked/ppt/_rels/presentation.xml.rels", encoding="utf-8").read()
    tgt = {}
    for m in re.finditer(r'<Relationship\b[^>]*/>', rels):
        tag = m.group(0)
        rid = re.search(r'Id="(rId\d+)"', tag)
        t = re.search(r'Target="slides/(slide\d+\.xml)"', tag)
        if rid and t:
            tgt[rid.group(1)] = t.group(1)
    return [tgt[r] for r in re.findall(r'<p:sldId[^>]*r:id="(rId\d+)"', pres)]


def set_title(path, title):
    s = io.open(path, encoding="utf-8").read()
    m = re.search(r'(sz="3200"[^>]*>.*?<a:t>)(.*?)(</a:t>)', s, re.S)
    assert m, path
    io.open(path, "w", encoding="utf-8", newline="").write(
        s[:m.start(2)] + esc(title) + s[m.end(2):])


def renumber(files):
    for i, f in enumerate(files, 1):
        p = os.path.join(SLIDES, f)
        s = io.open(p, encoding="utf-8").read()
        s2 = re.sub(r'<a:t>第 \d+ 頁</a:t>',
                    '<a:t>第 %d 頁</a:t>' % i, s)
        if s2 != s:
            io.open(p, "w", encoding="utf-8", newline="").write(s2)


def main():
    files = order_to_files()
    assert len(files) == 45, "expected 45 slides after insertion, got %d" % len(files)
    renumber(files)
    for pos, (title, fn) in zip(range(24, 29), BUILDERS):
        f = files[pos - 1]
        path = os.path.join(SLIDES, f)
        set_title(path, title)
        _rels[0] = eqlib.SlideRels(f)
        body = fn()
        _rels[0].save()
        s = io.open(path, encoding="utf-8").read()
        s = s.replace("</p:spTree>", body + "</p:spTree>")
        io.open(path, "w", encoding="utf-8", newline="").write(s)
        print("slide %-2d  %-44s %-14s +%d chars" % (pos, title, f, len(body)))


if __name__ == "__main__":
    main()
