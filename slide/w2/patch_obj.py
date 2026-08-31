# -*- coding: utf-8 -*-
"""Slide 25 revision:
  1. fold lambda into V, so the page states min D/V with V written out in metres;
  2. add the pod-tier (rho) explanation, which the T6 term needs to be readable;
  3. record every panel's inner box so the checker can verify panel containment,
     not just page containment (two blocks were quietly overflowing).
Constraint rows lose their per-row notes: the category headings already carry the
meaning, and the page has to make room for the objective block.
"""
import io

p = "build_m4g.py"
s = io.open(p, encoding="utf-8").read()


def sub(a, b):
    global s
    assert a in s, "MISS: " + a[:70]
    s = s.replace(a, b, 1)


# ── panels register themselves for the containment check ──────────────────
sub("_uid = [600]\n_rels = [None]",
    "_uid = [600]\n_rels = [None]\nPANELS = []")
sub('''def block(x, y, w, h, label):
    xml = (rect(x, y, w, BAR_H, NAVY)
           + tbox(x + 0.20, y + 0.08, w - 0.40, 0.30,
                  [para([run(label, 1800, WHITE, bold=True)], line=2060)])
           + rect(x, y + BAR_H, w, h - BAR_H, PANEL))
    return xml, (x + 0.24, y + BAR_H + 0.15, w - 0.48, h - BAR_H - 0.27)''',
    '''def block(x, y, w, h, label):
    xml = (rect(x, y, w, BAR_H, NAVY)
           + tbox(x + 0.20, y + 0.08, w - 0.40, 0.30,
                  [para([run(label, 1800, WHITE, bold=True)], line=2060)])
           + rect(x, y + BAR_H, w, h - BAR_H, PANEL))
    inner = (x + 0.24, y + BAR_H + 0.15, w - 0.48, h - BAR_H - 0.27)
    PANELS.append({"label": label, "x": inner[0], "y": inner[1],
                   "w": inner[2], "h": inner[3]})
    return xml, inner''')

# ── objective block: lambda inside V, plus the pod-tier note ──────────────
old_start = s.index("    # ── right / C : objective")
old_end = s.index("    # ── right / D : constraint delta")
sub(s[old_start:old_end], '''    # ── right / C : objective ──────────────────────────────────────────────
    o_h = 4.05
    b, (ix, iy, iw, ih) = block(RX, TOP, RW, o_h,
                                "Objective:  Fixed Weights  \\u2192  Measured Prices")
    xml += b
    cy = iy
    xml += tbox(ix, cy, 0.70, 0.26,
                [para([run("M1", 1300, NAVY, bold=True)], line=1520)])
    e, ew, eh = eq(r"\\min\\;\\; \\alpha_1 \\sum_{o,w} \\hat{x}_{o,w} \\;+\\; \\alpha_2 D"
                   r" \\;+\\; \\alpha_3 \\sum_{w} u_w"
                   r" \\qquad \\alpha_1,\\alpha_2,\\alpha_3 \\;\\mathrm{fixed}",
                   ix + 0.70, cy, 16)
    xml += e
    cy += max(eh, 0.26) + 0.18
    xml += tbox(ix, cy, 0.70, 0.26,
                [para([run("M4", 1300, NAVY, bold=True)], line=1520)])
    e, ew, eh = eq(r"\\min\\;\\; D \\,/\\, V", ix + 0.70, cy, 17, EQ_NAVY)
    xml += e
    cy += max(eh, 0.26) + 0.14
    e, ew, eh = eq(r"V = \\lambda\\!\\left[(1-\\beta)\\!\\sum_{o,i}\\! e_{o,i}"
                   r" + \\beta\\!\\sum_{o,i}\\! \\hat{e}_{o,i}\\right]"
                   r" + \\mu\\!\\left[(1-\\beta)\\!\\sum_{o}\\! f_{o}"
                   r" + \\beta\\!\\sum_{o}\\! \\hat{f}_{o}\\right]"
                   r" + \\varepsilon \\sum q"
                   r" + \\rho\\!\\left[\\sum_{p \\in P_p}\\! q - \\sum_{p \\in P_a}\\! q\\right]",
                   ix + 0.24, cy, 15)
    xml += e
    cy += eh + 0.12
    _n = ("Both sides are in metres. mu = distance per completed order and "
          "beta = bound over valued completions, both read off the run in progress; "
          "M1 pins the same ratio by hand at beta = |alpha-1| / alpha-3 = 40 / 1000. "
          "Solved by Dinkelbach as min D - V at the current lambda: mu, epsilon and "
          "rho scale with lambda, so only its level moves, never V's shape.")
    nh = text_h(len(_n), 1200, 1500, iw)
    xml += tbox(ix, cy, iw, nh + 0.05, [para([run(_n, 1200, MUTED)], line=1500)])
    cy += nh + 0.16
    xml += rect(ix, cy, iw, 0.020, NAVY)
    cy += 0.16
    xml += tbox(ix, cy, iw, 0.28,
                [para([run("Pod tier \\u03c1 \\u2014 binding layer only",
                           1350, NAVY, bold=True)], line=1620)])
    cy += 0.32
    for latex, note in [
        (r"p \\in P_p \\;\\;\\; -\\rho",
         "already at the station and being picked: taking the unit now saves a "
         "future trip for it"),
        (r"p \\in P_a \\;\\;\\; +\\rho",
         "dispatched by this decision: the unit genuinely costs an extra trip"),
        (r"p \\in P_q \\cup P_b \\;\\;\\; 0",
         "queued or en route: the trip is already sunk, as in T1 and T2"),
    ]:
        e, ew, eh = eq(latex, ix + 0.10, cy, 14)
        xml += e
        nh = text_h(len(note), 1150, 1420, iw - 1.70)
        xml += tbox(ix + 1.70, cy + max(0.0, (eh - nh) / 2.0), iw - 1.70, nh + 0.04,
                    [para([run(note, 1150, MUTED)], line=1420)])
        cy += max(eh, nh, 0.22) + 0.07

''')

# ── constraints: start lower, and drop the per-row notes ──────────────────
sub("    c_y = TOP + o_h + 0.16", "    c_y = TOP + o_h + 0.14")
sub("                cy += 0.30\n                continue", "                cy += 0.28\n                continue")
for a, b in [('r"\\sum_{w} \\hat{x}_{o,w} \\leq 1", "one order, one workstation"),',
             'r"\\sum_{w} \\hat{x}_{o,w} \\leq 1", None),'),
             ('r"\\delta_{o,p,w}", "subsumed by the draw variable"),',
              'r"\\delta_{o,p,w}", None),'),
             ('"binding within valuation, now per unit"),', 'None),'),
             ('"a draw needs stock on that pod at that workstation"),', 'None),'),
             ('r"\\sum_{p,w} \\hat{q}_{o,i,p,w} \\leq n_{i,o}",\n          "never draw beyond the residual demand"),',
              'r"\\sum_{p,w} \\hat{q}_{o,i,p,w} \\leq n_{i,o}", None),'),
             ('"a line closes only when drawn in full"),', 'None),'),
             ('"idle slots may now stay open"),', 'None),'),
             ('"occupies one of the slots at w"),', 'None),'),
             ('r"x_{o,w} \\leq \\sum_{i,p} q_{o,i,p,w}", "no slot without a draw"),',
              'r"x_{o,w} \\leq \\sum_{i,p} q_{o,i,p,w}", None),'),
             ('"pod and robot constraints carry over"),', 'None),')]:
    sub(a, b)

io.open(p, "w", encoding="utf-8", newline="").write(s)

# ── panels.json for the checker ───────────────────────────────────────────
sub2 = io.open(p, encoding="utf-8").read()
assert "PANELS" in sub2
io.open(p, "w", encoding="utf-8", newline="").write(
    sub2.replace('''    _rels[0].save()''',
                 '''    _rels[0].save()
    import json
    json.dump(PANELS, io.open("panels.json", "w", encoding="utf-8"))''', 1))
print("objective rewritten (lambda inside V, pod tier added); constraint notes dropped")
