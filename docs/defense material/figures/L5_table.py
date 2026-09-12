# -*- coding: utf-8 -*-
"""Large-scale phase-A table for the true fixed-lambda M5 arms (1 seed each)."""
import csv, glob, os
OUT = r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\out"
GRID = [12, 16, 22, 26, 30, 40, 52, 68, 88, 115]

def load(tag, declared=None):
    p = glob.glob(os.path.join(OUT, tag, "*", "statistics.txt"))
    if not p: return None
    d = {}
    for l in open(p[0], encoding="utf-8", errors="replace"):
        k, _, v = l.strip().partition(":")
        try: d[k.strip()] = float(v)
        except ValueError: pass
    f = glob.glob(os.path.join(OUT, tag, "*", "hgs_m5_decision_log.csv"))
    pin, n = float("nan"), 0
    if f:
        rows = [r for r in csv.DictReader(open(f[0], encoding="utf-8", errors="replace"))
                if int(r["dispatched"]) > 0]
        n = len(rows)
        if declared is not None and rows:
            pin = 100.0*sum(1 for r in rows if abs(float(r["lambdaEnd"])-declared) < 1e-6)/n
    L = d["StatOverallLinesHandled"]
    return dict(items=d["StatOverallItemsHandled"], orders=d["StatOverallOrdersHandled"],
                mpl=d["StatOverallDistanceTraveled"]/L, po=d["KPI_PO"], eor=d["KPI_EOR"],
                turn=d["StatMedianTurnoverTime"], late=d["StatOverallOrdersLate"], n=n, pin=pin)

hdr = "%-7s %8s %8s %9s %8s %8s %7s %6s %7s %9s"
print(hdr % ("λ", "件數", "訂單", "公尺/行", "pile-on", "EOR", "週期", "過期", "派車數", "釘住率%"))
print("-" * 84)
dyn = load("L5_dyn_s0")
if dyn:
    print(hdr % ("動態", "%.0f"%dyn["items"], "%.0f"%dyn["orders"], "%.3f"%dyn["mpl"],
                 "%.3f"%dyn["po"], "%.3f"%dyn["eor"], "%.0f"%dyn["turn"], "%.0f"%dyn["late"],
                 "%d"%dyn["n"], "-"))
for v in GRID:
    r = load("L5tf_%d_s0" % v, float(v))
    if not r: continue
    flag = "" if r["pin"] >= 95 else "  ✗"
    print(hdr % (v, "%.0f"%r["items"], "%.0f"%r["orders"], "%.3f"%r["mpl"], "%.3f"%r["po"],
                 "%.3f"%r["eor"], "%.0f"%r["turn"], "%.0f"%r["late"], "%d"%r["n"],
                 "%.1f"%r["pin"]) + flag)
