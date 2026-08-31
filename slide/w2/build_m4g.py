# -*- coding: utf-8 -*-
"""Slide 25 of Defense_aesop.pptx — the M4 model as the increment over M1.

Layout and type are lifted from slide 24 (the user's own refined M1 page):
  columns  left x=0.38 w=8.55   right x=9.31 w=10.30   content band y 1.15..9.80
  header   bar 1F3864 h=0.46, white Times New Roman Bold 18pt at +0.20/+0.08
  panel    EDF3FC        body 404040 13.5pt        note 595959 12pt
One navy family only; slide 24 uses no second hue.

Notation follows Jiao et al. (2026) — x-hat/x, z_{p,w}, y_{r,p}, delta_{o,p,w},
u_w, c_w(t), n_{i,o}, n-hat_{i,p} — and the new symbols dodge every one of them:
q for draws, e for line closure, f for order completion. The realisation rate is
BETA: the source paper already spends delta twice (trigger threshold p.4,
auxiliary variable p.6).

Model statements read off RAWSimO.Core/.../M4GManager.cs (R/V/B :722-1467,
objective T1-T6 :1503-1672) under the canonical m4g.xconf.
"""
import io, json, math

import eqlib
from eqlib import emu

SLIDE, SLIDE_NAME = "u/ppt/slides/slide2.xml", "slide2.xml"

NAVY, PANEL, WHITE = "1F3864", "EDF3FC", "FFFFFF"
BODY, MUTED = "404040", "595959"
TNR, TNRB = "Times New Roman", "Times New Roman Bold"
EQ_INK, EQ_NAVY = "#404040", "#1F3864"

LX, LW = 0.38, 8.55
RX, RW = 9.31, 10.30
TOP, BOT = 1.15, 9.80
BAR_H = 0.46
A_H, O_H = 2.44, 4.06

_uid, _rels, PANELS = [600], [None], []


def nid():
    _uid[0] += 1
    return _uid[0]


def esc(t):
    return t.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def run(text, sz=1350, color=BODY, bold=False):
    face = TNRB if bold else TNR
    a = 'lang="en-US" sz="%d"' % sz + (' b="true"' if bold else "")
    return ('<a:r><a:rPr %s><a:solidFill><a:srgbClr val="%s"/></a:solidFill>'
            '<a:latin typeface="%s"/><a:ea typeface="%s"/><a:cs typeface="%s"/>'
            '</a:rPr><a:t xml:space="preserve">%s</a:t></a:r>'
            % (a, color, face, face, face, esc(text)))


def para(runs, line=None):
    p = '<a:pPr algn="l">'
    if line:
        p += '<a:lnSpc><a:spcPts val="%d"/></a:lnSpc>' % line
    return "<a:p>" + p + "<a:buNone/></a:pPr>" + "".join(runs) + "</a:p>"


def text_h(chars, sz, lnspc, width):
    per = width / (0.48 * sz / 7200.0)
    return max(1, int(math.ceil(chars / per - 1e-6))) * lnspc / 7200.0


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


def eq(latex, x, y, fs=15, color=EQ_INK):
    name, w, h = eqlib.render(latex, fs, color)
    return eqlib.pic(nid(), _rels[0].image(name), x, y, w, h), w, h


def block(x, y, w, h, label):
    xml = (rect(x, y, w, BAR_H, NAVY)
           + tbox(x + 0.20, y + 0.08, w - 0.40, 0.30,
                  [para([run(label, 1800, WHITE, bold=True)], line=2060)])
           + rect(x, y + BAR_H, w, h - BAR_H, PANEL))
    inner = (x + 0.24, y + BAR_H + 0.15, w - 0.48, h - BAR_H - 0.27)
    PANELS.append({"label": label, "x": inner[0], "y": inner[1],
                   "w": inner[2], "h": inner[3]})
    return xml, inner


# ══════════════════════════════════════════════════════════════════════════
def build():
    xml = ""

    # ── left / atom ────────────────────────────────────────────────────────
    b, (ix, iy, iw, ih) = block(LX, TOP, LW, A_H,
                                "Decision Atom:  Order  \u2192  Item")
    xml += b
    cy = iy
    for tag, latex, note in [
        ("M1", r"\hat{x}_{o,w} \in \{0,1\}",
         "the ORDER is the atom: assigned whole, to one workstation, in one decision"),
        ("M4", r"\hat{q}_{o,i,p,w} \in \mathbb{Z}_{\geq 0}",
         "the ITEM is the atom: units of SKU i of order o drawn from pod p at w"),
    ]:
        e, ew, eh = eq(latex, ix + 0.62, cy, 18)
        xml += e
        xml += tbox(ix, cy + max(0.0, (eh - 0.24) / 2.0), 0.56, 0.26,
                    [para([run(tag, 1300, NAVY, bold=True)], line=1520)])
        nh = text_h(len(note), 1350, 1640, iw - 0.62)
        xml += tbox(ix + 0.62, cy + eh + 0.02, iw - 0.62, nh + 0.05,
                    [para([run(note, 1350, BODY)], line=1640)])
        cy += eh + 0.02 + nh + 0.14
    _c = ("Order splitting is not an added feature; it is what the finer atom makes "
          "representable.")
    xml += tbox(ix, cy + 0.02, iw, text_h(len(_c), 1300, 1580, iw) + 0.05,
                [para([run(_c, 1300, MUTED)], line=1580)])

    # ── left / variables ───────────────────────────────────────────────────
    v_y = TOP + A_H + 0.16
    b, (ix, iy, iw, ih) = block(LX, v_y, LW, BOT - v_y, "Decision Variables")
    xml += b
    cy = iy
    for head, rows in [
        ("Valuation layer", [
            (r"\hat{q}_{o,i,p,w} \in \mathbb{Z}_{\geq 0}", "valued draw"),
            (r"\hat{e}_{o,i} \in \{0,1\}", "line (o, i) is closed in full"),
            (r"\hat{f}_{o} \in \{0,1\}", "every line of order o is closed"),
        ]),
        ("Binding layer", [
            (r"q_{o,i,p,w} \in \mathbb{Z}_{\geq 0}", "executed draw"),
            (r"x_{o,w} \in \{0,1\}", "order o occupies a slot at w"),
            (r"e_{o,i},\; f_{o} \in \{0,1\}", "bound line closure and completion"),
        ]),
        ("Inherited from M1", [
            (r"z_{p,w},\; y_{r,p} \in \{0,1\}", "pod and robot assignment, unchanged"),
        ]),
    ]:
        xml += tbox(ix, cy, iw, 0.28,
                    [para([run(head, 1350, NAVY, bold=True)], line=1620)])
        cy += 0.32
        for latex, note in rows:
            e, ew, eh = eq(latex, ix + 0.18, cy, 16)
            xml += e
            xml += tbox(ix + 2.62, cy + max(0.0, (eh - 0.24) / 2.0), iw - 2.62, 0.28,
                        [para([run(note, 1300, BODY)], line=1560)])
            cy += max(eh, 0.26) + 0.08
        cy += 0.10
    xml += rect(ix, cy, iw, 0.020, NAVY)
    cy += 0.18
    xml += tbox(ix, cy, iw, 0.28,
                [para([run("No longer required", 1350, NAVY, bold=True)], line=1620)])
    cy += 0.32
    for latex, note in [
        (r"\delta_{o,p,w}",
         "the draw variable already names the (order, pod, workstation) triple, so "
         "this auxiliary variable and constraints (12)-(13) are subsumed"),
        (r"u_{w}",
         "with (4) relaxed to an inequality there is no unused capacity to price"),
    ]:
        e, ew, eh = eq(latex, ix + 0.18, cy, 16)
        xml += e
        nh = text_h(len(note), 1250, 1520, iw - 1.30)
        xml += tbox(ix + 1.30, cy + max(0.0, (eh - nh) / 2.0), iw - 1.30, nh + 0.05,
                    [para([run(note, 1250, MUTED)], line=1520)])
        cy += max(eh, nh, 0.24) + 0.10

    # ── right / objective ──────────────────────────────────────────────────
    b, (ix, iy, iw, ih) = block(RX, TOP, RW, O_H,
                                "Objective:  Fixed Weights  \u2192  Measured Prices")
    xml += b
    cy = iy
    xml += tbox(ix, cy, iw, 0.26, [para([
        run("M1 (14)", 1300, NAVY, bold=True),
        run("    a weighted linear sum of orders, distance and idle capacity; the "
            "three weights are fixed in advance.", 1300, BODY)], line=1560)])
    cy += 0.34
    xml += tbox(ix, cy, 0.78, 0.26,
                [para([run("M4", 1300, NAVY, bold=True)], line=1520)])
    e, ew, eh = eq(r"\min\;\; D \,/\, V", ix + 0.78, cy - 0.04, 17, EQ_NAVY)
    xml += e
    cy += max(eh, 0.26) + 0.12
    e, ew, eh = eq(r"V = \lambda\!\left[(1-\beta)\!\sum_{o,i}\! e_{o,i}"
                   r" + \beta\!\sum_{o,i}\! \hat{e}_{o,i}\right]"
                   r" + \mu\!\left[(1-\beta)\!\sum_{o}\! f_{o}"
                   r" + \beta\!\sum_{o}\! \hat{f}_{o}\right]"
                   r" + \varepsilon \sum q"
                   r" + \rho\!\left[\sum_{p \in P_p}\! q - \sum_{p \in P_a}\! q\right]",
                   ix + 0.10, cy, 15)
    xml += e
    cy += eh + 0.10
    _n = ("D and V are both in metres. mu = distance per completed order and "
          "beta = bound over valued completions, both read off the run in progress; "
          "M1 pins the same ratio by hand at 40 / 1000. Dinkelbach solves "
          "min D - V at the current lambda, and mu, epsilon and rho scale with it, "
          "so only the level moves and never V's shape.")
    nh = text_h(len(_n), 1200, 1490, iw)
    xml += tbox(ix, cy, iw, nh + 0.04, [para([run(_n, 1200, MUTED)], line=1490)])
    cy += nh + 0.14
    xml += rect(ix, cy, iw, 0.020, NAVY)
    cy += 0.15
    xml += tbox(ix, cy, iw, 0.26, [para([
        run("Pod tier  \u03c1  \u2014  binding layer only", 1300, NAVY, bold=True),
        run("    only bound draws have a real-world effect.",
            1250, MUTED)], line=1560)])
    cy += 0.32
    e, ew, eh = eq(r"p \in P_p:\; -\rho \qquad p \in P_a:\; +\rho"
                   r" \qquad p \in P_q \cup P_b:\; 0", ix + 0.10, cy, 15)
    xml += e
    cy += eh + 0.06
    _t = ("A unit from a pod being picked now saves a future trip; one from a pod "
          "this decision dispatches costs an extra trip. Queued and en-route pods "
          "are already sunk, as in T1 and T2.")
    th = text_h(len(_t), 1200, 1490, iw)
    xml += tbox(ix, cy, iw, th + 0.04, [para([run(_t, 1200, MUTED)], line=1490)])

    # ── right / constraint delta ───────────────────────────────────────────
    c_y = TOP + O_H + 0.14
    b, (ix, iy, iw, ih) = block(RX, c_y, RW, BOT - c_y,
                                "Constraints:  What Changes from M1")
    xml += b
    gap = 0.30
    cw = (iw - gap) / 2.0
    cols = [
        [("H", "Removed"),
         ("E", r"\mathrm{(2)}\;\; \sum_{w} \hat{x}_{o,w} \leq 1"),
         ("E", r"\mathrm{(12),(13)}\;\; \delta_{o,p,w}"),
         ("H", "Replaced"),
         ("E", r"\mathrm{(3)} \rightarrow \mathrm{B1}\;\;"
               r" q_{o,i,p,w} \leq \hat{q}_{o,i,p,w}"),
         ("E", r"\mathrm{(5)} \rightarrow \mathrm{V1}\;\;"
               r" \sum_{o} \hat{q}_{o,i,p,w} \leq \hat{n}_{i,p}\, z_{p,w}"),
         ("E", r"\mathrm{V2}\;\; \sum_{p,w} \hat{q}_{o,i,p,w} \leq n_{i,o}"),
         ("E", r"\mathrm{V3}\;\; \sum_{p,w} \hat{q}_{o,i,p,w}"
               r" \geq n_{i,o}\, \hat{e}_{o,i}"),
         ("H", "Relaxed"),
         ("E", r"\mathrm{(4)} \rightarrow \mathrm{B3}\;\;"
               r" \sum_{o} x_{o,w} \leq c_w(t)"),
         ],
        [("H", "Added \u2014 completion is no longer implied"),
         ("E", r"\mathrm{V4}\;\; \hat{e}_{o,i} \geq \hat{f}_{o}"),
         ("E", r"\mathrm{B2}\;\; \sum_{i,p} q_{o,i,p,w} \leq N_{o}\, x_{o,w}"),
         ("E", r"\mathrm{B4}\;\; x_{o,w} \leq \sum_{i,p} q_{o,i,p,w}"),
         ("E", r"\mathrm{B5}\;\; \sum_{p,w} q_{o,i,p,w} \geq n_{i,o}\, e_{o,i}"),
         ("E", r"\mathrm{B6}\;\; e_{o,i} \leq \hat{e}_{o,i}, \;\;"
               r" f_{o} \leq \hat{f}_{o}"),
         ("E", r"\mathrm{B7}\;\; e_{o,i} \geq f_{o}"),
         ("H", "Unchanged"),
         ("E", r"\mathrm{(6)-(11)} \;\rightarrow\; \mathrm{R1-R5}"),
         ],
    ]
    for ci, col in enumerate(cols):
        cx, cy = ix + ci * (cw + gap), iy
        for kind, item in col:
            if kind == "H":
                xml += tbox(cx, cy, cw, 0.26,
                            [para([run(item, 1300, NAVY, bold=True)], line=1560)])
                cy += 0.30
                continue
            e, ew, eh = eq(item, cx + 0.10, cy, 14)
            xml += e
            cy += max(eh, 0.24) + 0.075
    return xml


def main():
    _rels[0] = eqlib.SlideRels(SLIDE_NAME)
    body = build()
    _rels[0].save()
    json.dump(PANELS, io.open("panels.json", "w", encoding="utf-8"))
    s = io.open(SLIDE, encoding="utf-8").read()
    assert "</p:spTree>" in s
    io.open(SLIDE, "w", encoding="utf-8", newline="").write(
        s.replace("</p:spTree>", body + "</p:spTree>"))
    print("slide2.xml patched  (+%d chars)" % len(body))


if __name__ == "__main__":
    main()
