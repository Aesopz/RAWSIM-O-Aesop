# -*- coding: utf-8 -*-
"""Page 26 — how the ratio objective is actually solved, and where the prices come from.
The two things page 25 states but does not explain.

Grid measured off the user's own page 25:
  left x=0.38 w=8.55   right x=9.31 w=10.30   band y 1.15..9.80   bar h=0.46
  bar 1F3864 / white Times New Roman Bold 18pt at +0.20/+0.08   panel EDF3FC
  body 404040 13.5pt   note 595959 12pt

Every statement is read off the source, not recalled:
  Dinkelbach loop      M4GManager.cs:1786-1840   (tolerance, cap, escalation branch)
  price formulas       M4GPricing.cs:143-206
  measured values      out/b6_split_s0/.../m4g_decision_log.csv  (small, 6 bots, seed 0)
"""
import io, json, math

import eqlib
from eqlib import emu

SLIDE, SLIDE_NAME = "u/ppt/slides/slide3.xml", "slide3.xml"

NAVY, PANEL, WHITE = "1F3864", "EDF3FC", "FFFFFF"
BODY, MUTED = "404040", "595959"
TNR, TNRB = "Times New Roman", "Times New Roman Bold"
EQ_INK, EQ_NAVY = "#404040", "#1F3864"

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


def note(x, y, w, txt, sz=1250, line=1520):
    h = text_h(len(txt), sz, line, w)
    return tbox(x, y, w, h + 0.05, [para([run(txt, sz, MUTED)], line=line)]), h + 0.05


A_H, C_H = 2.72, 3.84


# ══════════════════════════════════════════════════════════════════════════
def build():
    xml = ""

    # ── left / A : why a ratio cannot go into the solver ───────────────────
    b, (ix, iy, iw, ih) = block(LX, TOP, LW, A_H,
                                "Why the Ratio Cannot Be Solved Directly")
    xml += b
    cy = iy
    e, ew, eh = eq(r"\min\;\; D \,/\, V", ix, cy, 18, EQ_NAVY)
    xml += e
    n, nh = note(ix + 1.90, cy + 0.02, iw - 1.90,
                 "A MILP solver minimises a LINEAR expression. Division is not linear, "
                 "so this cannot be handed to the solver as written.")
    xml += n
    cy += max(eh, nh) + 0.20
    xml += rect(ix, cy, iw, 0.020, NAVY)
    cy += 0.18
    e, ew, eh = eq(r"D - \lambda V < 0 \quad\Longleftrightarrow\quad D / V < \lambda",
                   ix, cy, 16)
    xml += e
    cy += eh + 0.10
    n, nh = note(ix, cy, iw,
                 "Read the transform as a budget: at the guessed rate lambda, V units of "
                 "value are worth lambda-V metres, and D is what the plan actually spends. "
                 "Under budget means the plan beat the rate.")
    xml += n

    # ── left / B : the test and the loop ───────────────────────────────────
    b_y = TOP + A_H + 0.16
    b, (ix, iy, iw, ih) = block(LX, b_y, LW, BOT - b_y,
                                "The Test, and the Loop")
    xml += b
    cy = iy
    e, ew, eh = eq(r"F(\lambda) \;=\; \min_{x} \left[\, D(x) - \lambda V(x) \,\right]",
                   ix, cy, 16, EQ_NAVY)
    xml += e
    cy += eh + 0.14
    for cond, means, act in [
        (r"F(\lambda) < 0", "some plan beats the rate", "lower lambda"),
        (r"F(\lambda) > 0", "no plan reaches it", "raise lambda"),
        (r"F(\lambda) = 0", "the best plan exactly breaks even", "lambda is optimal"),
    ]:
        e, ew, eh = eq(cond, ix + 0.10, cy, 15)
        xml += e
        xml += tbox(ix + 1.62, cy + max(0.0, (eh - 0.24) / 2.0), iw - 1.62, 0.26,
                    [para([run(means, 1300, BODY),
                           run("      →  " + act, 1300, NAVY, bold=True)],
                          line=1560)])
        cy += max(eh, 0.26) + 0.08
    cy += 0.06
    n, nh = note(ix, cy, iw,
                 "F is decreasing in lambda, so the root is unique: the ratio problem "
                 "becomes a one-dimensional root search.")
    xml += n
    cy += nh + 0.18
    xml += rect(ix, cy, iw, 0.020, NAVY)
    cy += 0.18
    for i, (head, body) in enumerate([
        ("Guess a rate", "start from the running measured value"),
        ("Solve the linear subproblem", "min  D − lambda-V   — this the solver can do"),
        ("Read the rate that plan achieved", "D* / V*"),
        ("Adopt it and repeat", "until the objective is within tolerance"),
    ], 1):
        xml += tbox(ix, cy, iw, 0.28, [para([
            run("%d.  " % i, 1300, NAVY, bold=True),
            run(head, 1350, BODY, bold=True),
            run("      " + body, 1250, MUTED)], line=1600)])
        cy += 0.30
    cy += 0.10
    e, ew, eh = eq(r"\lambda_{k+1} \;=\; D^{*} / V^{*}", ix + 0.10, cy, 16, EQ_NAVY)
    xml += e
    n, nh = note(ix + 2.60, cy + 0.01, iw - 2.60,
                 "this is Newton's method on F, hence the quadratic convergence")
    xml += n
    cy += max(eh, nh) + 0.16
    n, nh = note(ix, cy, iw,
                 "Measured: 96 % of decisions converge within a single update "
                 "(0 updates 444, 1 update 449, of 929). Stop at |objective| <= 0.5 m or "
                 "5 iterations. The theorem needs V > 0; doing nothing gives V = 0, so "
                 "that case doubles lambda instead, at most 6 times.")
    xml += n

    # ── right / C : where the prices come from ─────────────────────────────
    b, (ix, iy, iw, ih) = block(RX, TOP, RW, C_H, "Where the Prices Come From")
    xml += b
    cy = iy
    xml += tbox(ix, cy, iw, 0.50, [para([
        run("Travel distance is the only measured quantity.", 1350, BODY, bold=True),
        run("  Each price is that distance divided by a different counter of work done.",
            1350, BODY)], line=1620)])
    cy += 0.58
    cols = [0.0, 1.15, 5.30, 7.35]
    for lab, sz_ in [("", 0), ("formula", 1250), ("unit", 1250), ("median", 1250)]:
        pass
    xml += tbox(ix + cols[1], cy, 4.0, 0.24,
                [para([run("derived from", 1200, MUTED)], line=1440)])
    xml += tbox(ix + cols[2], cy, 1.9, 0.24,
                [para([run("unit", 1200, MUTED)], line=1440)])
    xml += tbox(ix + cols[3], cy, 2.2, 0.24,
                [para([run("measured median", 1200, MUTED)], line=1440)])
    cy += 0.28
    for sym, formula, unit, med in [
        (r"\lambda", "distance / closed lines", "metres per line", "8.06"),
        (r"\mu", "lambda x lines per order", "metres per order", "12.22"),
        (r"\rho", "distance / units picked", "metres per unit", "3.70"),
        (r"\varepsilon", "0.001 x lambda", "metres per unit", "0.008"),
        (r"\beta", "bound lines / valued lines", "dimensionless", "0.188"),
    ]:
        e, ew, eh = eq(sym, ix + 0.14, cy, 17, EQ_NAVY)
        xml += e
        xml += tbox(ix + cols[1], cy + 0.01, 4.0, 0.26,
                    [para([run(formula, 1300, BODY)], line=1560)])
        xml += tbox(ix + cols[2], cy + 0.01, 1.9, 0.26,
                    [para([run(unit, 1250, MUTED)], line=1560)])
        xml += tbox(ix + cols[3], cy + 0.01, 2.2, 0.26,
                    [para([run(med, 1300, NAVY, bold=True)], line=1560)])
        cy += max(eh, 0.28) + 0.06
    cy += 0.06
    n, nh = note(ix, cy, iw,
                 "No price is set by the modeller. The hand-set numbers are the warm-up "
                 "seeds, used for the first 50 closed lines only, and the epsilon scale "
                 "— a tie-break three orders of magnitude below lambda.")
    xml += n

    # ── right / D : the two invariants ─────────────────────────────────────
    d_y = TOP + C_H + 0.16
    b, (ix, iy, iw, ih) = block(RX, d_y, RW, BOT - d_y,
                                "Two Invariants the Design Rests On")
    xml += b
    cy = iy
    xml += tbox(ix, cy, iw, 0.28, [para([
        run("1.  The price ratios are fixed by the instance, not chosen.",
            1400, NAVY, bold=True)], line=1660)])
    cy += 0.34
    e, ew, eh = eq(r"V(\lambda) \;=\; \lambda \cdot \tilde{V}"
                   r"\qquad \mu/\lambda,\;\; \rho/\lambda,\;\; \varepsilon/\lambda"
                   r"\;\; \mathrm{constant}", ix + 0.10, cy, 15)
    xml += e
    cy += eh + 0.08
    n, nh = note(ix, cy, iw,
                 "Dinkelbach only works if V keeps its SHAPE while lambda searches, so "
                 "every price scales with lambda together and only the level moves. "
                 "mu / lambda is lines per order; rho / lambda is the reciprocal of units "
                 "per line — both properties of the instance.")
    xml += n
    cy += nh + 0.20
    xml += rect(ix, cy, iw, 0.020, NAVY)
    cy += 0.18
    xml += tbox(ix, cy, iw, 0.28, [para([
        run("2.  beta is bounded on both sides, and the measured value sits inside.",
            1400, NAVY, bold=True)], line=1660)])
    cy += 0.34
    hdr = [("beta", 1.20), ("what the model does", 4.60), ("result", 3.90)]
    cx = ix + 0.10
    for lab, w in hdr:
        xml += tbox(cx, cy, w - 0.15, 0.24,
                    [para([run(lab, 1200, MUTED)], line=1440)])
        cx += w
    cy += 0.28
    for val, does, res, hi in [
        ("0", "credits a pod only for the line it can bind now",
         "pile-on 6.04 → 4.13,  energy +24.5 %", True),
        ("0.19", "credits unusable coverage at the measured rate",
         "the canonical setting", False),
        ("≥ 0.5", "execution earns nothing at the margin",
         "the run stops producing orders", True),
    ]:
        cx = ix + 0.10
        xml += tbox(cx, cy, hdr[0][1] - 0.15, 0.26,
                    [para([run(val, 1350, NAVY, bold=True)], line=1600)])
        cx += hdr[0][1]
        xml += tbox(cx, cy, hdr[1][1] - 0.15, 0.26,
                    [para([run(does, 1300, BODY)], line=1600)])
        cx += hdr[1][1]
        xml += tbox(cx, cy, hdr[2][1] - 0.10, 0.26,
                    [para([run(res, 1250, MUTED if not hi else BODY)], line=1600)])
        cy += 0.27
    cy += 0.05
    n, nh = note(ix, cy, iw,
                 "beta is conditioned on how many pods are already committed: slot "
                 "capacity pins the binding layer near one line per decision while the "
                 "valuation layer grows with committed supply, so a further pod is scored "
                 "for coverage the binding layer cannot take.")
    xml += n
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
    print("slide3.xml patched  (+%d chars)" % len(body))


if __name__ == "__main__":
    main()
