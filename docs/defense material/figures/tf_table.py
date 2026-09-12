# -*- coding: utf-8 -*-
"""Phase-A table for the true fixed-lambda arms (1 seed each)."""
import csv, glob, os, sys
OUT = r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\out"
GRID = [("3","3"),("4p5","4.5"),("6p5","6.5"),("9","9"),("13","13"),
        ("18","18"),("26","26"),("36","36"),("50","50")]

def load(tag, declared=None, log="m4g"):
    p = glob.glob(os.path.join(OUT, tag, "*", "statistics.txt"))
    if not p: return None
    d = {}
    for l in open(p[0], encoding="utf-8", errors="replace"):
        k, _, v = l.strip().partition(":")
        try: d[k.strip()] = float(v)
        except ValueError: pass
    f = glob.glob(os.path.join(OUT, tag, "*", "%s_decision_log.csv" % log))
    pin = float("nan"); n = 0
    if f:
        rows = list(csv.DictReader(open(f[0], encoding="utf-8", errors="replace")))
        dd = [r for r in rows if int(r["newTrips" if log=="m4g" else "dispatched"]) > 0]
        n = len(dd)
        if declared is not None and dd:
            pin = 100.0*sum(1 for r in dd if abs(float(r["lambdaEnd"])-declared) < 1e-6)/len(dd)
    L = d["StatOverallLinesHandled"]
    return dict(items=d["StatOverallItemsHandled"], orders=d["StatOverallOrdersHandled"],
                mpl=d["StatOverallDistanceTraveled"]/L, po=d["KPI_PO"], eor=d["KPI_EOR"],
                turn=d["StatMedianTurnoverTime"], late=d["StatOverallOrdersLate"], n=n, pin=pin)

hdr = "%-7s %7s %7s %8s %8s %7s %7s %6s %7s %8s"
print(hdr % ("λ", "件數", "訂單", "公尺/行", "pile-on", "EOR", "週期", "過期", "派車數", "釘住率%"))
print("-" * 80)
dyn = load("su_dyn_s0")
print(hdr % ("動態", "%.0f"%dyn["items"], "%.0f"%dyn["orders"], "%.3f"%dyn["mpl"],
             "%.3f"%dyn["po"], "%.3f"%dyn["eor"], "%.0f"%dyn["turn"], "%.0f"%dyn["late"],
             "%d"%dyn["n"], "-"))
for tag, lab in GRID:
    r = load("tfA_%s_s0" % tag, float(lab))
    if not r: continue
    flag = "" if r["pin"] >= 95 else "  ✗不合格"
    print(hdr % (lab, "%.0f"%r["items"], "%.0f"%r["orders"], "%.3f"%r["mpl"], "%.3f"%r["po"],
                 "%.3f"%r["eor"], "%.0f"%r["turn"], "%.0f"%r["late"], "%d"%r["n"],
                 "%.1f"%r["pin"]) + flag)
