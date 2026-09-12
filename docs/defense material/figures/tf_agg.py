# -*- coding: utf-8 -*-
"""Aggregate true-fixed-lambda arms over seeds and test each against the dynamic arm.

Writes arms_tf.json for the figure scripts and prints the decision table.
Usage:  python tf_agg.py <nseeds> <lam> [<lam> ...]
"""
import csv, glob, json, math, os, sys, statistics as st
OUT = r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\out"
SP  = os.path.dirname(os.path.abspath(__file__))
TCRIT = {3: 3.182, 4: 2.776, 9: 2.262}

def one(tag, declared=None):
    p = glob.glob(os.path.join(OUT, tag, "*", "statistics.txt"))
    if not p: return None
    d = {}
    for l in open(p[0], encoding="utf-8", errors="replace"):
        k, _, v = l.strip().partition(":")
        try: d[k.strip()] = float(v)
        except ValueError: pass
    pin, n = float("nan"), 0
    f = glob.glob(os.path.join(OUT, tag, "*", "m4g_decision_log.csv"))
    if f:
        rows = [r for r in csv.DictReader(open(f[0], encoding="utf-8", errors="replace"))
                if int(r["newTrips"]) > 0]
        n = len(rows)
        if declared is not None and rows:
            pin = 100.0*sum(1 for r in rows if abs(float(r["lambdaEnd"])-declared) < 1e-6)/n
    L = d["StatOverallLinesHandled"]
    return {"items": d["StatOverallItemsHandled"], "orders": d["StatOverallOrdersHandled"],
            "dist": d["StatOverallDistanceTraveled"], "mpl": d["StatOverallDistanceTraveled"]/L,
            "pileon": d["KPI_PO"], "eor": d["KPI_EOR"], "turn": d["StatMedianTurnoverTime"],
            "late": d["StatOverallOrdersLate"], "dispatches": n, "pin": pin}

def arm(pat, ns, declared=None):
    runs = [one(pat % s, declared) for s in range(ns)]
    if any(r is None for r in runs): return None
    rec = {"n": ns, "seeds": runs}
    for m in runs[0]:
        v = [r[m] for r in runs]
        if not all(x == x for x in v):        # NaN (pin is undefined for the dynamic arm)
            rec[m] = float("nan"); rec[m+"_sd"] = float("nan"); continue
        rec[m] = st.mean(v)
        rec[m+"_sd"] = st.stdev(v) if ns > 1 else 0.0
    return rec

NS = int(sys.argv[1])
LAMS = [float(x) for x in sys.argv[2:]]
dyn = arm("su_dyn_s%d", NS)
arms = []
for lam in LAMS:
    tag = ("%g" % lam).replace(".", "p")
    a = arm("tfA_" + tag + "_s%d", NS, lam)
    if a: a["lam"] = lam; arms.append(a)

# 方向：正值 = 動態較優
DIR = {"items": "hi", "orders": "hi", "pileon": "hi",
       "dist": "lo", "mpl": "lo", "eor": "lo", "turn": "lo", "late": "lo"}
LAB = {"items": "件數", "orders": "訂單", "dist": "總距離", "mpl": "公尺/行",
       "pileon": "pile-on", "eor": "EOR", "turn": "週期中位", "late": "過期訂單"}

tc = TCRIT.get(NS-1, 2.0)
print("動態臂（%d seeds）：件數 %.1f  公尺/行 %.3f  pile-on %.3f  EOR %.3f  週期 %.0f  過期 %.1f"
      % (NS, dyn["items"], dyn["mpl"], dyn["pileon"], dyn["eor"], dyn["turn"], dyn["late"]))
print()
for a in arms:
    print("λ=%-5g  釘住率 %.1f%%  (n=%d seeds)" % (a["lam"], a["pin"], a["n"]))
    for m in ("items", "mpl", "pileon", "eor", "turn", "late"):
        d = [x[m]-y[m] for x, y in zip(dyn["seeds"], a["seeds"])]
        sd = st.stdev(d)
        t = st.mean(d)/(sd/math.sqrt(len(d))) if sd else float("nan")
        rel = 100*st.mean(d)/a[m] if a[m] else 0.0
        if DIR[m] == "lo": rel = -rel
        sig = "*" if (t == t and abs(t) >= tc) else "n.s."
        print("    %-10s 動態 %9.3f  λ臂 %9.3f   動態優勢 %+7.2f%%  t=%+6.2f %s"
              % (LAB[m], dyn[m], a[m], rel, t, sig))
    print()

json.dump({"dyn": dyn, "fixed": arms}, open(os.path.join(SP, "arms_tf.json"), "w"), indent=1)
print("→ arms_tf.json")
