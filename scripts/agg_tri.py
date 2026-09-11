# -*- coding: utf-8 -*-
"""Three-arm table (M1G / M4G-NS / M4G) plus the NS-lin robustness ablation.

Every arm in this script must come from the same binary and the same
layout/xsett pair, otherwise the paired tests below compare two things at once.
"""
import os, csv, glob, math, statistics as st

ROOT = r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"

# (kpi key, label, higher-is-better)
KEYS = [("orders_completed", "完成訂單", True),
        ("items_handled", "件數", True),
        ("lines_handled", "行數", True),
        ("system_order_pile_on", "pile-on", True),
        ("energy_mech_per_order_kJ", "EOR", False),
        ("total_distance_m", "總距離", False)]

# statistics.txt carries the item/line counters the KPI report does not expose.
STATS = {"StatOverallItemsHandled": "items_handled",
         "StatOverallLinesHandled": "lines_handled"}

ARMS = [("M1G", "tri_m1g"), ("M4G-NS", "nsdink"), ("M4G", "tri_m4g")]


def kpi(run):
    hit = glob.glob(os.path.join(ROOT, "out", run, "*", "kpi_report.csv"))
    if not hit:
        return None
    out = {}
    for row in csv.reader(open(hit[0])):
        if len(row) >= 5 and row[0] == "L1":
            try:
                out[row[1]] = float(row[4])
            except ValueError:
                pass
    stats = os.path.join(os.path.dirname(hit[0]), "statistics.txt")
    if os.path.exists(stats):
        for line in open(stats, encoding="utf-8", errors="replace"):
            name, sep, val = line.partition(":")
            if sep and name.strip() in STATS:
                try:
                    out[STATS[name.strip()]] = float(val)
                except ValueError:
                    pass
    return out


def arm(prefix, seeds=range(5)):
    return [kpi("%s_s%d" % (prefix, s)) for s in seeds]


def paired_t(a, b):
    d = [x - y for x, y in zip(a, b)]
    if len(d) < 2:
        return float("nan")
    s = st.stdev(d)
    return float("inf") if s == 0 else (sum(d) / len(d)) / (s / math.sqrt(len(d)))


def col(rows, key):
    return [r[key] for r in rows if r and key in r]


def main():
    data = {name: arm(pfx) for name, pfx in ARMS}
    for name, rows in data.items():
        got = sum(1 for r in rows if r)
        print("%-8s seeds=%d" % (name, got))
        if got < 5:
            print("  ⚠ 尚未跑滿 5 seeds，下表僅供參考")
    print()

    print("%-10s %12s %12s %12s" % ("指標", "M1G", "M4G-NS", "M4G"))
    for key, label, _ in KEYS:
        vals = [col(data[n], key) for n, _ in ARMS]
        if not all(vals):
            continue
        print("%-10s %12.3f %12.3f %12.3f"
              % (label, st.mean(vals[0]), st.mean(vals[1]), st.mean(vals[2])))

    # The two attribution steps: pricing/objective, then atom.
    for lo, hi, title in [("M1G", "M4G-NS", "M1G → M4G-NS（目標式：手調線性 → 自我校準分式）"),
                          ("M4G-NS", "M4G", "M4G-NS → M4G（原子：整單 → 件級）")]:
        print("\n" + title)
        print("  %-10s %9s %9s" % ("指標", "Δ%", "t"))
        for key, label, _ in KEYS:
            a, b = col(data[hi], key), col(data[lo], key)
            n = min(len(a), len(b))
            if n < 2:
                continue
            a, b = a[:n], b[:n]
            pct = (st.mean(a) - st.mean(b)) / st.mean(b) * 100
            t = paired_t(a, b)
            mark = "" if abs(t) < 2.776 else "  *"   # two-sided 0.05, df=4
            print("  %-10s %+8.2f%% %9.2f%s" % (label, pct, t, mark))
    print("\n* = |t| > 2.776（雙尾 0.05, df=4）")


if __name__ == "__main__":
    main()
