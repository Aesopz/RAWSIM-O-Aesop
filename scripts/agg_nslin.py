# -*- coding: utf-8 -*-
"""Aggregate the m4g_ns_lin vs m4g_ns (vs M1G) 5-seed comparison."""
import os, csv, glob, math, statistics as st

ROOT = r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
KEYS = [("orders_completed", "完成訂單", 1),
        ("system_order_pile_on", "pile-on", 1),
        ("energy_mech_per_order_kJ", "EOR", -1),
        ("total_distance_m", "總距離", -1)]


def kpi(run):
    d = glob.glob(os.path.join(ROOT, "out", run, "*", "kpi_report.csv"))
    if not d:
        return None
    out = {}
    for r in csv.reader(open(d[0])):
        if len(r) >= 5 and r[0] == "L1":
            try:
                out[r[1]] = float(r[4])
            except ValueError:
                pass
    return out


def paired_t(a, b):
    d = [x - y for x, y in zip(a, b)]
    n = len(d)
    if n < 2:
        return float("nan")
    m = sum(d) / n
    s = st.stdev(d)
    return float("inf") if s == 0 else m / (s / math.sqrt(n))


def arm(prefix, seeds=range(5)):
    rows = [kpi("%s_s%d" % (prefix, s)) for s in seeds]
    return [r for r in rows if r]


def report(name_a, a, name_b, b):
    print("\n%s  vs  %s   (n=%d)" % (name_a, name_b, min(len(a), len(b))))
    print("  %-10s %10s %10s %9s %8s" % ("指標", name_a, name_b, "Δ%", "t"))
    for k, label, _ in KEYS:
        va = [r[k] for r in a if k in r]
        vb = [r[k] for r in b if k in r]
        n = min(len(va), len(vb))
        va, vb = va[:n], vb[:n]
        ma, mb = st.mean(va), st.mean(vb)
        pct = (ma - mb) / mb * 100 if mb else float("nan")
        print("  %-10s %10.3f %10.3f %+8.2f%% %8.2f"
              % (label, ma, mb, pct, paired_t(va, vb)))


if __name__ == "__main__":
    lin, dink = arm("nslin"), arm("nsdink")
    print("nslin seeds=%d  nsdink seeds=%d" % (len(lin), len(dink)))
    if lin and dink:
        report("NS-lin", lin, "NS-dink", dink)
