# -*- coding: utf-8 -*-
"""One page for M4: the atom, the four constraints that carry a modelling choice,
why the objective is a ratio, how the ratio is solved, and what rho and beta mean.

Diagram-led. No measured values anywhere on this page.

Grid taken from the user's own page 24:
  left x=0.38 w=8.55   right x=9.31 w=10.30   band y 1.15..9.80   bar h=0.46
  bar 1F3864 / white Times New Roman Bold 18pt   panel EDF3FC
  body 404040 13.5pt   note 595959 12pt
"""
import io, json, math

import eqlib
from eqlib import emu

SLIDE, SLIDE_NAME = "u/ppt/slides/slide2.xml", "slide2.xml"

NAVY, PANEL, WHITE = "1F3864", "EDF3FC", "FFFFFF"
BODY, MUTED = "404040", "595959"
COST, VAL = "B4531A", "0E7C86"          # distance side / value side
TNR, TNRB = "Times New Roman", "Times New Roman Bold"
EQ_INK, EQ_NAVY = "#404040", "#1F3864"
EQ_COST, EQ_VAL = "#B4531A", "#0E7C86"

LX, LW = 0.38, 8.55
RX, RW = 9.31, 10.30
TOP, BOT = 1.15, 9.80
BAR_H = 0.46

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


def para(runs, line=None, align="l"):
    p = '<a:pPr algn="%s">' % align
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


def shape(prst, x, y, w, h, fill=None, line=None, lw=1.0, flipH=False, flipV=False,
          arrow=False, adj=""):
    i = nid()
    f = ('<a:solidFill><a:srgbClr val="%s"/></a:solidFill>' % fill) if fill else "<a:noFill/>"
    if line:
        ln = ('<a:ln w="%d" cap="rnd"><a:solidFill><a:srgbClr val="%s"/></a:solidFill>%s</a:ln>'
              % (int(lw * 12700), line,
                 '<a:tailEnd type="triangle" w="med" len="med"/>' if arrow else ""))
    else:
        ln = "<a:ln><a:noFill/></a:ln>"
    fl = (' flipH="1"' if flipH else "") + (' flipV="1"' if flipV else "")
    return ('<p:sp><p:nvSpPr><p:cNvPr name="Draw %d" id="%d"/><p:cNvSpPr/><p:nvPr/>'
            '</p:nvSpPr><p:spPr><a:xfrm%s><a:off x="%d" y="%d"/><a:ext cx="%d" cy="%d"/>'
            '</a:xfrm><a:prstGeom prst="%s">%s</a:prstGeom>%s%s</p:spPr>'
            '<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang="en-US"/></a:p>'
            '</p:txBody></p:sp>'
            % (i, i, fl, emu(x), emu(y), emu(max(w, 0.004)), emu(max(h, 0.004)),
               prst, adj or "<a:avLst/>", f, ln))


def rect(x, y, w, h, fill):
    return shape("rect", x, y, w, h, fill=fill)


def seg(x1, y1, x2, y2, color=NAVY, lw=1.4, arrow=False):
    return shape("line", min(x1, x2), min(y1, y2), abs(x2 - x1), abs(y2 - y1),
                 line=color, lw=lw, flipH=(x2 < x1), flipV=(y2 < y1), arrow=arrow)


def eq(latex, x, y, fs=15, color=EQ_INK, cw=None):
    name, w, h = eqlib.render(latex, fs, color)
    if cw is not None:
        x = x + (cw - w) / 2.0
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


def cap(x, y, w, txt, sz=1250, color=MUTED, align="l"):
    h = text_h(len(txt), sz, int(sz * 1.22), w)
    return tbox(x, y, w, h + 0.05,
                [para([run(txt, sz, color)], line=int(sz * 1.22), align=align)]), h + 0.05


A_H, C_H = 2.62, 5.34


# ══════════════════════════════════════════════════════════════════════════
def build():
    xml = ""

    # ── LEFT / A : the atom ────────────────────────────────────────────────
    b, (ix, iy, iw, ih) = block(LX, TOP, LW, A_H, "The Decision Atom")
    xml += b
    cy = iy
    # order row
    xml += rect(ix + 0.10, cy, 3.05, 0.42, "D8DEE8")
    xml += tbox(ix + 0.10, cy + 0.09, 3.05, 0.26,
                [para([run("one order", 1300, NAVY, bold=True)], line=1560, align="ctr")])
    xml += seg(ix + 3.35, cy + 0.21, ix + 3.95, cy + 0.21, MUTED, 1.2, arrow=True)
    for k in range(4):
        xml += rect(ix + 4.15 + k * 0.52, cy, 0.44, 0.42, "CFE3E5")
    e, ew, eh = eq(r"o \;\; i \;\; p \;\; w", ix + 4.15, cy + 0.06, 15, EQ_VAL, cw=2.02)
    xml += e
    cy += 0.42 + 0.16
    e, ew, eh = eq(r"q_{o,i,p,w} \in \mathbb{Z}_{\geq 0}", ix + 0.10, cy, 19, EQ_VAL)
    xml += e
    c, ch = cap(ix + 3.30, cy + 0.06, iw - 3.30,
                "units of SKU i of order o, drawn from pod p, at station w", 1300, BODY)
    xml += c
    cy += max(eh, ch) + 0.16
    c, ch = cap(ix + 0.10, cy, iw - 0.10,
                "Splitting is what this variable permits — not a feature added on top.")
    xml += c

    # ── LEFT / B : only the constraints that carry a choice ────────────────
    b_y = TOP + A_H + 0.16
    b, (ix, iy, iw, ih) = block(LX, b_y, LW, BOT - b_y,
                                "Constraints That Carry a Modelling Choice")
    xml += b
    cy = iy
    for tag, latex, why in [
        ("V1", r"\sum_{o} \hat{q}_{o,i,p,w} \;\leq\; \hat{n}_{i,p}\, z_{p,w}",
         "draw only what that pod carries, and only if it is sent"),
        ("V3", r"\sum_{p,w} \hat{q}_{o,i,p,w} \;\geq\; n_{i,o}\, \hat{e}_{o,i}",
         "a line counts only when it is drawn in full"),
        ("B1", r"q_{o,i,p,w} \;\leq\; \hat{q}_{o,i,p,w}",
         "what is executed is a subset of what is valued"),
        ("B3", r"\sum_{o} x_{o,w} \;\leq\; c_{w}(t)",
         "a station holds a fixed number of orders at a time"),
    ]:
        e, ew, eh = eq(latex, ix + 0.86, cy, 16)
        xml += e
        xml += tbox(ix, cy + max(0.0, (eh - 0.24) / 2.0), 0.80, 0.26,
                    [para([run(tag, 1300, NAVY, bold=True)], line=1560)])
        c, ch = cap(ix + 0.86, cy + max(eh, 0.26) + 0.02, iw - 0.86, why, 1250)
        xml += c
        cy += max(eh, 0.26) + 0.02 + ch + 0.16
    cy += 0.06
    xml += rect(ix, cy, iw, 0.020, NAVY)
    cy += 0.16
    c, ch = cap(ix, cy, iw,
                "Pod and robot constraints — one pod per station, one robot per pod, "
                "inheritance of assignments already committed — are the physical rules "
                "of the floor and are carried over unchanged.")
    xml += c

    # ── RIGHT / C : the ratio, and how it is solved ────────────────────────
    b, (ix, iy, iw, ih) = block(RX, TOP, RW, C_H,
                                "Why a Ratio, and How It Is Solved")
    xml += b

    # the fraction, drawn as a fraction
    fx, fy, fw = ix + 0.20, iy, 2.55
    e, ew, eh = eq(r"\min", fx - 0.02, fy + 0.42, 19, EQ_NAVY)
    xml += e
    xml += rect(fx + 0.72, fy, fw, 0.42, "F6E2D4")
    xml += tbox(fx + 0.72, fy + 0.09, fw, 0.26,
                [para([run("D    distance walked", 1300, COST, bold=True)],
                      line=1560, align="ctr")])
    xml += rect(fx + 0.72, fy + 0.50, fw, 0.032, NAVY)
    xml += rect(fx + 0.72, fy + 0.62, fw, 0.42, "D5EAEB")
    xml += tbox(fx + 0.72, fy + 0.71, fw, 0.26,
                [para([run("V    value delivered", 1300, VAL, bold=True)],
                      line=1560, align="ctr")])
    c, ch = cap(fx + 0.72 + fw + 0.34, fy + 0.10, iw - fw - 1.30,
                "metres walked per unit of value delivered. A weighted sum would first "
                "need an answer to “how many metres is an order worth”. "
                "A ratio does not.", 1300, BODY)
    xml += c
    cy = fy + 1.04 + 0.22
    xml += rect(ix, cy, iw, 0.020, NAVY)
    cy += 0.20

    # the transform
    e, ew, eh = eq(r"D - \lambda V < 0 \quad\Longleftrightarrow\quad D/V < \lambda",
                   ix + 0.10, cy, 16)
    xml += e
    c, ch = cap(ix + 4.60, cy + 0.02, iw - 4.60,
                "a solver minimises linear expressions, not divisions — so guess the "
                "rate λ and ask whether any plan beats it", 1250)
    xml += c
    cy += max(eh, ch) + 0.20

    # F(lambda) curve
    ax0, ax1 = ix + 0.55, ix + 4.55
    ay = cy + 0.95
    xml += seg(ax0 - 0.18, ay, ax1 + 0.22, ay, MUTED, 1.0)                # lambda axis
    xml += seg(ax0, ay + 0.62, ax0, ay - 0.98, MUTED, 1.0)                 # F axis
    pts = [(0.15, 0.72), (1.40, 0.40), (2.50, 0.00), (3.70, -0.52)]
    for j in range(len(pts) - 1):
        x1, v1 = pts[j]
        x2, v2 = pts[j + 1]
        xml += seg(ax0 + x1, ay - v1, ax0 + x2, ay - v2, NAVY, 2.0)
    xml += shape("ellipse", ax0 + 2.50 - 0.055, ay - 0.055, 0.11, 0.11, fill=VAL)
    e, ew, eh = eq(r"F(\lambda)", ax0 - 0.10, ay - 1.30, 14, EQ_NAVY)
    xml += e
    e, ew, eh = eq(r"\lambda", ax1 + 0.28, ay - 0.10, 14)
    xml += e
    e, ew, eh = eq(r"\lambda^{*}", ax0 + 2.42, ay + 0.14, 14, EQ_VAL)
    xml += e
    xml += tbox(ax0 + 0.16, ay - 0.94, 1.60, 0.24,
                [para([run("F > 0   raise", 1200, MUTED)], line=1420)])
    xml += tbox(ax0 + 2.72, ay + 0.34, 1.70, 0.24,
                [para([run("F < 0   lower", 1200, MUTED)], line=1420)])

    # the loop, to the right of the curve
    lx = ix + 5.30
    e, ew, eh = eq(r"F(\lambda) = \min_{x}\left[\, D - \lambda V \,\right]", lx, cy, 15,
                   EQ_NAVY)
    xml += e
    ly = cy + eh + 0.16
    for j, t in enumerate([
        "guess a rate  λ",
        "solve  min  D − λV",
        "read the rate it achieved",
        "adopt it, repeat",
    ], 1):
        xml += tbox(lx, ly, iw - 5.30, 0.26, [para([
            run("%d  " % j, 1250, VAL, bold=True), run(t, 1300, BODY)], line=1560)])
        ly += 0.29
    e, ew, eh = eq(r"\lambda_{k+1} = D^{*} / V^{*}", lx + 0.10, ly + 0.04, 15, EQ_VAL)
    xml += e
    c, ch = cap(lx, ly + eh + 0.14, iw - 5.30,
                "F is decreasing, so the root is unique; the update is Newton's method "
                "on F.  [Dinkelbach 1967]", 1200)
    xml += c

    # ── RIGHT / D : what the two coefficients mean ─────────────────────────
    d_y = TOP + C_H + 0.16
    b, (ix, iy, iw, ih) = block(RX, d_y, RW, BOT - d_y,
                                "What the Two Coefficients Mean")
    xml += b
    half = (iw - 0.50) / 2.0

    # -- beta
    xml += tbox(ix, iy, half, 0.28, [para([
        run("β", 1500, VAL, bold=True),
        run("    seen  →  done", 1350, BODY)], line=1660)])
    by = iy + 0.36
    bh = 1.05
    xml += rect(ix + 0.14, by, 0.62, bh, "CFE3E5")
    xml += rect(ix + 1.30, by + bh * 0.80, 0.62, bh * 0.20, VAL)
    xml += rect(ix + 1.30, by, 0.62, bh * 0.80, "EDF3FC")
    xml += tbox(ix + 0.05, by + bh + 0.04, 0.80, 0.24,
                [para([run("valued", 1150, MUTED)], line=1380, align="ctr")])
    xml += tbox(ix + 1.21, by + bh + 0.04, 0.80, 0.24,
                [para([run("bound", 1150, VAL)], line=1380, align="ctr")])
    xml += seg(ix + 0.78, by + 0.02, ix + 1.28, by + bh * 0.80, MUTED, 1.0)
    xml += seg(ix + 0.78, by + bh, ix + 1.28, by + bh, MUTED, 1.0)
    e, ew, eh = eq(r"\beta = \frac{\mathrm{bound}}{\mathrm{valued}}", ix + 2.15, by + 0.16,
                   15, EQ_VAL)
    xml += e
    c, ch = cap(ix + 2.15, by + 0.16 + eh + 0.10, half - 2.15,
                "the share of what the model can see that it can execute now", 1200)
    xml += c
    c, ch = cap(ix, by + bh + 0.40, half,
                "a bound line earns the full price; a line only seen earns "
                "λβ", 1250, BODY)
    xml += c

    # -- rho
    rx0 = ix + half + 0.50
    xml += tbox(rx0, iy, half, 0.28, [para([
        run("ρ", 1500, VAL, bold=True),
        run("    which pod you drain", 1350, BODY)], line=1660)])
    ry = iy + 0.40
    for k, (lab, price, col, pc) in enumerate([
        ("being picked now", "− ρ", "CFE3E5", VAL),
        ("dispatched now", "+ ρ", "F6E2D4", COST),
        ("still en route", "0", "E4E7EC", MUTED),
    ]):
        px = rx0 + k * (half / 3.0)
        pw = half / 3.0 - 0.22
        xml += shape("roundRect", px, ry, pw, 0.50, fill=col,
                     adj='<a:avLst><a:gd name="adj" fmla="val 18000"/></a:avLst>')
        xml += tbox(px, ry + 0.13, pw, 0.26,
                    [para([run(price, 1400, pc, bold=True)], line=1660, align="ctr")])
        xml += tbox(px - 0.06, ry + 0.56, pw + 0.12, 0.26,
                    [para([run(lab, 1150, MUTED)], line=1380, align="ctr")])
    c, ch = cap(rx0, ry + 0.92, half,
                "taking a unit from a pod about to leave saves a future trip; taking one "
                "from a pod fetched now costs an extra trip. ρ prices which pod is "
                "drained, not how far it travelled.", 1250, BODY)
    xml += c
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
    print("slide2.xml rebuilt  (+%d chars)" % len(body))


if __name__ == "__main__":
    main()
