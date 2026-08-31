# -*- coding: utf-8 -*-
"""Geometry self-check for the methodology slides (shape id > 400).

No renderer is available on this machine, so text fit is estimated: Times New
Roman averages ~0.48 em advance for mixed English, and a paragraph's height is
its wrapped line count times its lnSpc. Equation pictures are placed at their
true rendered size, so those are exact.

Checks: (a) nothing leaves the content band, (b) no text box overflows its own
height, (c) no two foreground shapes (text boxes, equation images) overlap.
"""
import io, math, re, sys
from defusedxml import minidom

E = 914400.0
L, R, TOP, BOT = 0.385, 19.615, 1.45, 10.10
FOOT_Y = 10.32
AVG_EM = 0.48


def order_to_files():
    pres = io.open("unpacked/ppt/presentation.xml", encoding="utf-8").read()
    rels = io.open("unpacked/ppt/_rels/presentation.xml.rels", encoding="utf-8").read()
    tgt = {}
    for m in re.finditer(r"<Relationship\b[^>]*/>", rels):
        tag = m.group(0)
        rid = re.search(r'Id="(rId\d+)"', tag)
        t = re.search(r'Target="slides/(slide\d+\.xml)"', tag)
        if rid and t:
            tgt[rid.group(1)] = t.group(1)
    return [tgt[r] for r in re.findall(r'<p:sldId[^>]*r:id="(rId\d+)"', pres)]


def txt_of(sp):
    return "".join(t.firstChild.data if t.firstChild else ""
                   for t in sp.getElementsByTagName("a:t"))


def est_height(sp, width):
    total = 0.0
    for p in sp.getElementsByTagName("a:p"):
        runs = p.getElementsByTagName("a:r")
        if not runs:
            continue
        units, top_sz = 0.0, 0
        for r in runs:
            rpr = r.getElementsByTagName("a:rPr")
            sz = int(rpr[0].getAttribute("sz") or 1500) if rpr else 1500
            t = r.getElementsByTagName("a:t")
            s = t[0].firstChild.data if t and t[0].firstChild else ""
            units += len(s) * sz
            top_sz = max(top_sz, sz)
        lines = max(1, int(math.ceil(units * AVG_EM / 7200.0 / width - 1e-6)))
        ln = p.getElementsByTagName("a:spcPts")
        lh = int(ln[0].getAttribute("val")) if ln else int(top_sz * 1.2)
        before = 0
        bef = p.getElementsByTagName("a:spcBef")
        if bef:
            v = bef[0].getElementsByTagName("a:spcPts")
            if v:
                before = int(v[0].getAttribute("val"))
        total += lines * lh / 7200.0 + before / 7200.0
    return total


def boxes_of(path):
    d = minidom.parse(path)
    out = []
    for tag in ("p:sp", "p:pic"):
        for sp in d.getElementsByTagName(tag):
            pr = sp.getElementsByTagName("p:cNvPr")
            if not pr:
                continue
            try:
                sid = int(pr[0].getAttribute("id"))
            except ValueError:
                continue
            if sid <= 400:
                continue
            off = sp.getElementsByTagName("a:off")
            ext = sp.getElementsByTagName("a:ext")
            if not off or not ext:
                continue
            out.append({
                "name": pr[0].getAttribute("name"),
                "x": int(off[0].getAttribute("x")) / E,
                "y": int(off[0].getAttribute("y")) / E,
                "w": int(ext[0].getAttribute("cx")) / E,
                "h": int(ext[0].getAttribute("cy")) / E,
                "sp": sp, "kind": tag})
    return out


def overlap(a, b, pad=0.012):
    ox = min(a["x"] + a["w"], b["x"] + b["w"]) - max(a["x"], b["x"])
    oy = min(a["y"] + a["h"], b["y"] + b["h"]) - max(a["y"], b["y"])
    return ox > pad and oy > pad


def check(path, label):
    bad = []
    bx = boxes_of(path)
    fg = []
    for s in bx:
        if s["x"] < L - 0.01 or s["x"] + s["w"] > R + 0.01:
            bad.append("%s  x %.2f..%.2f outside %.2f..%.2f"
                       % (s["name"], s["x"], s["x"] + s["w"], L, R))
        if s["y"] < TOP - 0.01 or s["y"] + s["h"] > BOT + 0.02:
            bad.append("%s  y %.2f..%.2f outside %.2f..%.2f"
                       % (s["name"], s["y"], s["y"] + s["h"], TOP, BOT))
        if s["y"] + s["h"] > FOOT_Y:
            bad.append("%s  collides with the footer band" % s["name"])
        if s["kind"] == "p:sp" and s["sp"].getElementsByTagName("a:t"):
            need = est_height(s["sp"], s["w"])
            if need > s["h"] + 0.02:
                bad.append("%s  text needs ~%.2f\" in %.2f\"  (+%.2f)  «%s»"
                           % (s["name"], need, s["h"], need - s["h"],
                              txt_of(s["sp"])[:52]))
        if s["name"].startswith(("Text", "Equation")):
            fg.append(s)
    for i in range(len(fg)):
        for j in range(i + 1, len(fg)):
            if overlap(fg[i], fg[j]):
                bad.append("%s  overlaps  %s   at (%.2f,%.2f)/(%.2f,%.2f)"
                           % (fg[i]["name"], fg[j]["name"], fg[i]["x"], fg[i]["y"],
                              fg[j]["x"], fg[j]["y"]))
    eqs = sum(1 for s in bx if s["kind"] == "p:pic")
    print("── %s   %d shapes, %d equations" % (label, len(bx), eqs))
    for b in bad:
        print("   ! " + b)
    if not bad:
        print("   ok — in band, no overflow, no overlap")
    return len(bad)


if __name__ == "__main__":
    files = order_to_files()
    n = 0
    for pos in range(24, 29):
        n += check("unpacked/ppt/slides/" + files[pos - 1],
                   "slide %d  (%s)" % (pos, files[pos - 1]))
    print("\n%d issue(s)" % n)
    sys.exit(0)
