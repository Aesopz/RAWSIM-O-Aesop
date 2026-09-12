# -*- coding: utf-8 -*-
"""M4G-family ablation, 2026-09-12 design.

Part 1 (validation)  M1G  vs  M4G framework carrying M1G's own weights.
                     Order-atomic no-split, i.e. M1G's shi5 semantics, so a
                     null result here means the port is faithful.

Part 2 (attribution) One no-split definition throughout - ForbidSplitting +
                     ForbidCrossStationOnly, the lifetime-single-station arm.
    A  no split | M1G hand-tuned weights (incl. the w3=1000 idle-slot guardrail)
    B  no split | measured prices (no guardrail)      A->B = pricing
    C  SPLIT    | measured prices                     B->C = splitting
"""
import os, csv, glob, math, statistics as st

ROOT = r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
TCRIT = {1: 12.706, 2: 4.303, 3: 3.182, 4: 2.776}

KEYS = [("items_handled",            "件數",     True),
        ("lines_handled",            "行數",     True),
        ("orders_completed",         "完成訂單", True),
        ("system_order_pile_on",     "pile-on",  True),
        ("total_distance_m",         "總距離",   False),
        ("dist_per_line",            "公尺/行",  False),
        ("energy_mech_per_order_kJ", "EOR",      False),
        ("rest_pct",                 "休息%",    False),
        ("turnover_median",          "週期中位", False),
        ("orders_late",              "逾期單數", False)]

STATS = {"StatOverallItemsHandled": "items_handled",
         "StatOverallLinesHandled": "lines_handled",
         "StatOverallOrdersLate":   "orders_late",
         "StatAverageLatenessSec":  "lateness_avg",
         "StatMedianTurnoverTime":  "turnover_median",
         "StatTimeRestSec":         "rest_sec"}

BOT_SECONDS = 6 * 7200.0          # 6 bots, 2 h


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
    if out.get("lines_handled"):
        out["dist_per_line"] = out["total_distance_m"] / out["lines_handled"]
    if "rest_sec" in out:
        out["rest_pct"] = out["rest_sec"] / BOT_SECONDS * 100
    return out


def paired_t(a, b):
    d = [x - y for x, y in zip(a, b)]
    if len(d) < 2:
        return float("nan"), 0
    s = st.stdev(d)
    t = float("inf") if s == 0 else (sum(d) / len(d)) / (s / math.sqrt(len(d)))
    return t, len(d) - 1


def load(prefix, n):
    return [r for r in (kpi("%s_s%d" % (prefix, s)) for s in range(n)) if r]


def table(arms, data, n):
    print(("%-10s" + "%14s" * len(arms)) % (("指標",) + tuple(a for a, _ in arms)))
    print("-" * (10 + 14 * len(arms)))
    for key, label, _ in KEYS:
        vals = [[r[key] for r in data[a][:n] if key in r] for a, _ in arms]
        if not all(len(v) == n for v in vals):
            continue
        print(("%-10s" + "%14.3f" * len(arms))
              % ((label,) + tuple(st.mean(v) for v in vals)))


def step(data, lo, hi, n, title):
    print("\n" + title)
    print("  %-10s %10s %9s %6s" % ("指標", "Δ%", "t", ""))
    for key, label, _ in KEYS:
        a = [r[key] for r in data[hi][:n] if key in r]
        b = [r[key] for r in data[lo][:n] if key in r]
        if len(a) != n or len(b) != n:
            continue
        base = st.mean(b)
        pct = (st.mean(a) - base) / base * 100 if base else float("nan")
        t, df = paired_t(a, b)
        print("  %-10s %+9.2f%% %9.2f %6s"
              % (label, pct, t, "*" if abs(t) > TCRIT.get(df, 2.776) else "n.s."))


def main(n=5):
    arms = [("S0 M1G",           "ch4_P0"),
            ("S1 NS.M1G目標式",  "nc_S1"),
            ("S2 NS.mu定價",     "nc_S2"),
            ("S3 M4G拆單",       "ch4_P3")]
    data = {name: load(pfx, n) for name, pfx in arms}
    for k, v in data.items():
        print("%-18s seeds=%d" % (k, len(v)))
    n = min(len(v) for v in data.values())
    if n < 2:
        print("\n資料不足。")
        return
    print("\n配對 seeds = %d, t(0.05, df=%d) = %.3f\n"
          % (n, n - 1, TCRIT.get(n - 1, 2.776)))
    table(arms, data, n)
    step(data, arms[0][0], arms[1][0], n,
         "S0 -> S1  框架移植（M4G-NS 框架承載 M1G 目標式：mu=40=w1, sigma=25mu=w3, delta=1）")
    step(data, arms[1][0], arms[2][0], n,
         "S1 -> S2  M1G 目標式 -> 量測的訂單口徑定價")
    step(data, arms[2][0], arms[3][0], n,
         "S2 -> S3  不拆單 -> 拆單")
    step(data, arms[1][0], arms[3][0], n,
         "S1 -> S3  兩項貢獻合計")


if __name__ == "__main__":
    main()
