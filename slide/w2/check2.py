# -*- coding: utf-8 -*-
"""Geometry self-check for the M4 page (shape id > 600), against the content band
slide 1 actually uses: x 0.38..19.61, y 1.15..9.80."""
import io, math, sys
from defusedxml import minidom

E = 914400.0
L, R, TOP, BOT = 0.38, 19.61, 1.15, 9.80
AVG_EM = 0.48


def txt_of(sp):
    return "".join(t.firstChild.data if t.firstChild else ""
                   for t in sp.getElementsByTagName("a:t"))


def est_height(sp, width):
    total = 0.0
    for p in sp.getElementsByTagName("a:p"):
        runs = p.getElementsByTagName("a:r")
        if not runs:
            continue
        units, top = 0.0, 0
        for r in runs:
            rpr = r.getElementsByTagName("a:rPr")
            sz = int(rpr[0].getAttribute("sz") or 1350) if rpr else 1350
            t = r.getElementsByTagName("a:t")
            units += len(t[0].firstChild.data if t and t[0].firstChild else "") * sz
            top = max(top, sz)
        lines = max(1, int(math.ceil(units * AVG_EM / 7200.0 / width - 1e-6)))
        ln = p.getElementsByTagName("a:spcPts")
        total += lines * (int(ln[0].getAttribute("val")) if ln else top * 12 // 10) / 7200.0
    return total


def main(path):
    d = minidom.parse(path)
    boxes, bad = [], []
    for tag in ("p:sp", "p:pic"):
        for sp in d.getElementsByTagName(tag):
            pr = sp.getElementsByTagName("p:cNvPr")
            if not pr:
                continue
            try:
                sid = int(pr[0].getAttribute("id"))
            except ValueError:
                continue
            if sid <= 600:
                continue
            off, ext = sp.getElementsByTagName("a:off"), sp.getElementsByTagName("a:ext")
            if not off or not ext:
                continue
            b = {"n": pr[0].getAttribute("name"), "k": tag,
                 "x": int(off[0].getAttribute("x")) / E,
                 "y": int(off[0].getAttribute("y")) / E,
                 "w": int(ext[0].getAttribute("cx")) / E,
                 "h": int(ext[0].getAttribute("cy")) / E, "sp": sp}
            boxes.append(b)
            if b["x"] < L - 0.01 or b["x"] + b["w"] > R + 0.01:
                bad.append("%s  x %.2f..%.2f out of band" % (b["n"], b["x"], b["x"] + b["w"]))
            if b["y"] < TOP - 0.01 or b["y"] + b["h"] > BOT + 0.02:
                bad.append("%s  y %.2f..%.2f out of band  «%s»"
                           % (b["n"], b["y"], b["y"] + b["h"], txt_of(sp)[:44]))
            if tag == "p:sp" and sp.getElementsByTagName("a:t"):
                need = est_height(sp, b["w"])
                if need > b["h"] + 0.02:
                    bad.append("%s  text needs ~%.2f\" in %.2f\"  «%s»"
                               % (b["n"], need, b["h"], txt_of(sp)[:44]))
    # panel containment: every foreground item must sit inside the panel it
    # belongs to, not merely inside the page band
    try:
        import json
        panels = json.load(io.open("panels.json", encoding="utf-8"))
    except Exception:
        panels = []
    labels = {q["label"] for q in panels}
    for b in boxes:
        if not b["n"].startswith(("Text", "Equation")):
            continue
        if txt_of(b["sp"]).strip() in labels:   # a header bar's own label
            continue
        home = [q for q in panels
                if q["x"] - 0.30 <= b["x"] <= q["x"] + q["w"] + 0.05
                and q["y"] - 0.02 <= b["y"] < q["y"] + q["h"] + 0.60]
        if not home:
            continue
        q = min(home, key=lambda z: abs(z["y"] - b["y"]))
        if b["y"] + b["h"] > q["y"] + q["h"] + 0.02:
            bad.append("%s  spills %.2f\" past panel 「%s」  «%s»"
                       % (b["n"], b["y"] + b["h"] - q["y"] - q["h"],
                          q["label"][:26], txt_of(b["sp"])[:34]))

    fg = [b for b in boxes if b["n"].startswith(("Text", "Equation"))]
    for i in range(len(fg)):
        for j in range(i + 1, len(fg)):
            a, b = fg[i], fg[j]
            if (min(a["x"] + a["w"], b["x"] + b["w"]) - max(a["x"], b["x"]) > 0.012 and
                    min(a["y"] + a["h"], b["y"] + b["h"]) - max(a["y"], b["y"]) > 0.012):
                bad.append("%s overlaps %s  at (%.2f,%.2f)" % (a["n"], b["n"], a["x"], a["y"]))
    print("%d shapes, %d equations" % (len(boxes), sum(1 for b in boxes if b["k"] == "p:pic")))
    for x in bad:
        print("  ! " + x)
    if not bad:
        print("  ok — in band, no overflow, no overlap")
    return len(bad)


if __name__ == "__main__":
    sys.exit(0 if main("u/ppt/slides/slide2.xml") == 0 else 0)
