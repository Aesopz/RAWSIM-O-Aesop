# -*- coding: utf-8 -*-
"""epsilon tie-break ablation: canon (eps=0.001*lambda) vs eps=0, seed 0."""
import os, csv, glob, statistics as st
ROOT = r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
KEYS = [("orders_completed", "完成訂單", True),
        ("items_handled", "件數", True),
        ("lines_handled", "行數", True),
        ("system_order_pile_on", "pile-on", True),
        ("energy_mech_per_order_kJ", "EOR", False),
        ("total_distance_m", "總距離", False)]
STATS = {"StatOverallItemsHandled": "items_handled",
         "StatOverallLinesHandled": "lines_handled"}

def kpi(run):
    hit = glob.glob(os.path.join(ROOT, "out", run, "*", "kpi_report.csv"))
    if not hit: return None, None
    out = {}
    for row in csv.reader(open(hit[0])):
        if len(row) >= 5 and row[0] == "L1":
            try: out[row[1]] = float(row[4])
            except ValueError: pass
    d = os.path.dirname(hit[0])
    stats = os.path.join(d, "statistics.txt")
    if os.path.exists(stats):
        for line in open(stats, encoding="utf-8", errors="replace"):
            n, sep, v = line.partition(":")
            if sep and n.strip() in STATS:
                try: out[STATS[n.strip()]] = float(v)
                except ValueError: pass
    return out, d

def solve(d):
    p = os.path.join(d, "m4g_decision_log.csv")
    rows = list(csv.DictReader(open(p)))
    s = [float(r["solveSec"]) for r in rows]
    trips = sum(float(r["newTrips"]) for r in rows)
    return len(s), st.median(s), sum(s), max(s), trips

A, da = kpi("tri_m4g_s0")   # canon
B, db = kpi("m4geps0_s0")   # eps = 0
print("%-10s %14s %14s %10s" % ("指標", "canon", "eps=0", "差異%"))
for k, lab, hib in KEYS:
    a, b = A.get(k), B.get(k)
    if a is None or b is None: continue
    print("%-10s %14.3f %14.3f %+9.2f%%" % (lab, a, b, (b-a)/a*100))
for lab, d in (("canon", da), ("eps=0", db)):
    n, med, tot, mx, tr = solve(d)
    print("%-6s 決策 %d  solveSec 中位 %.4f  總計 %.1f  最大 %.2f  newTrips %.0f" % (lab, n, med, tot, mx, tr))
