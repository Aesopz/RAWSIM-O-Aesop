# -*- coding: utf-8 -*-
"""Slide 24 grew when it was restated in the paper's notation (14 constraint rows
instead of 12, plus the deviation note). Tighten it back into the content band."""
import io

p = "build_methodology.py"
s = io.open(p, encoding="utf-8").read()


def sub(a, b):
    global s
    assert a in s, "MISS: " + a[:70]
    s = s.replace(a, b, 1)


# cons_grid gains an adjustable row gap
sub("def cons_grid(x, y, w, h, items, cols=2, fs=17, gap=0.30, note_sz=1300):",
    "def cons_grid(x, y, w, h, items, cols=2, fs=17, gap=0.30, note_sz=1300,\n"
    "              row_gap=0.19):")
sub("            cy += row + 0.19", "            cy += row + row_gap")

# panels: give the constraint grid the space the two extra rows need
sub("    sp_h = 4.02", "    sp_h = 3.52")
sub("    o_h = 2.66", "    o_h = 2.40")

# (15)/(16) are domain declarations, not constraints between entities: state them
# once under the grid instead of spending two grid rows on them.
sub('''        ("(15)", r"\\hat{x}_{o,w},\\, x_{o,w},\\, z_{p,w},\\, \\delta_{o,p,w},\\,"
                 r" y_{r,p} \\in \\{0,1\\}", "binary domains"),
        ("(16)", r"u_w \\in \\mathbb{Z}^{+}_{0} \\quad \\forall w \\in W_a(t)",
         "unused capacity is a non-negative integer"),
    ]
    xml += cons_grid(ix, iy, iw, ih, items, cols=2, fs=15)
    return xml''',
    '''    ]
    xml += cons_grid(ix, iy, iw, ih, items, cols=2, fs=15, note_sz=1200,
                     row_gap=0.15)
    e4, _, eh4 = eq(r"(15)\\;\\; \\hat{x}_{o,w},\\, x_{o,w},\\, z_{p,w},\\,"
                    r" \\delta_{o,p,w},\\, y_{r,p} \\in \\{0,1\\}"
                    r" \\qquad (16)\\;\\; u_w \\in \\mathbb{Z}^{+}_{0}",
                    ix, iy + ih - 0.30, 14)
    xml += e4
    return xml''')

io.open(p, "w", encoding="utf-8", newline="").write(s)
print("slide 24 tightened")
