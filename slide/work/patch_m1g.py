# -*- coding: utf-8 -*-
"""Rewrite s_m1g() in build_methodology.py so slide 24 states the baseline in the
notation of Jiao et al. (2026) — Eqs (2)-(16) and the (19) preprocessing — and
declares where the implementation departs from the paper."""
import io

NEW = r'''def s_m1g():
    """Baseline M1G in the notation of Jiao et al. (2026), Eqs (2)-(16) and (19).
    The symbol map to the implementation is stated on the slide itself."""
    xml = ""
    lw = 8.55
    rw = R - L - lw - 0.38
    rx = L + lw + 0.38

    # ── sets and parameters ────────────────────────────────────────────────
    sp_h = 4.02
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
    o_h = 2.66
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
                    r" \bigl| \hat{O}(t) \bigr| > 0", ix, cy, 16)
    xml += e3
    _p = ("Preprocessing (19): orders that have waited longer than tau take "
          "priority, so long-waiting orders are not starved by the objective.")
    ph = text_h([(len(_p), 1300, 1580, 0)], iw - 3.20)
    xml += tbox(ix + 3.20, cy + max(0.0, (eh3 - ph) / 2.0), iw - 3.20, ph + 0.05,
                [para([run(_p, 1300, MUTED)], line=1580)])
    cy += max(eh3, ph) + 0.14
    _o = ("The weights are exogenous: the authors state they were determined by a "
          "series of preliminary experiments, and that the long-term objective's "
          "influence cannot be accurately quantified.")
    oh = text_h([(len(_o), 1300, 1580, 0)], iw)
    xml += tbox(ix, cy, iw, oh + 0.05, [para([run(_o, 1300, MUTED)], line=1580)])

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
        ("(15)", r"\hat{x}_{o,w},\, x_{o,w},\, z_{p,w},\, \delta_{o,p,w},\,"
                 r" y_{r,p} \in \{0,1\}", "binary domains"),
        ("(16)", r"u_w \in \mathbb{Z}^{+}_{0} \quad \forall w \in W_a(t)",
         "unused capacity is a non-negative integer"),
    ]
    xml += cons_grid(ix, iy, iw, ih, items, cols=2, fs=15)
    return xml


'''

p = "build_methodology.py"
s = io.open(p, encoding="utf-8").read()
a = s.index("def s_m1g():")
b = s.index("# 2 — Key Observation")
b = s.rindex("# " + "═" * 74, a, b)
io.open(p, "w", encoding="utf-8", newline="").write(s[:a] + NEW + s[b:])
print("s_m1g rewritten in the paper's notation")
