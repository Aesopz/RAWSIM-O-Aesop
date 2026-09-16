# -*- coding: utf-8 -*-
"""Standard CSV bundle (REPORTING-STANDARD 5.5) from an exp.py experiment's stats.json.
usage: python scripts/csv_bundle.py <experiment_id> <out_dir> <label_map.json>
label_map: {"arm_label": ["Policy name", bots], ...}; order of keys = row order."""
import sys, io, json, csv, glob, re, os, shutil
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import stats_pipeline as SP, exp as E
eid, out, lm = sys.argv[1], sys.argv[2], json.load(io.open(sys.argv[3], encoding="utf-8"))
d = E.exp_dir(eid); S = json.load(io.open(os.path.join(d, "stats.json"), encoding="utf-8")); ex = E.load_exp(eid)
os.makedirs(out, exist_ok=True); M = SP.METRICS; H = ["Policy", "Bots"] + [SP.APA_HEAD[k] for k in M]
def w(name, rows):
    with io.open(os.path.join(out, name), "w", encoding="utf-8-sig", newline="") as f: csv.writer(f).writerows(rows)
w("table1_means.csv", [H] + [lm[a] + [SP.fmt(S["descriptives"][a][k]["mean"], SP.DECIMALS[k]) for k in M] for a in lm])
w("table2_sd.csv", [H] + [lm[a] + [SP.fmt(S["descriptives"][a][k]["sd"], SP.DECIMALS[k]) for k in M] for a in lm])
hd = ["Policy", "Reference", "Bots"] + [SP.APA_HEAD[k] for k in M]; dl = [hd]; pv = [hd]; ci = [["Policy", "Reference", "Bots", "Bound"] + [SP.APA_HEAD[k] for k in M]]
for c in ex["comparisons"]:
    P = S["paired"]["%s vs %s" % (c["b"], c["a"])]; head = [lm[c["b"]][0], lm[c["a"]][0], lm[c["b"]][1]]
    dl.append(head + [SP.fmt_signed(P[k]["pct"], 2) + (P[k]["label"] if P[k]["label"] in ("*", "**", "***") else "") for k in M])
    pv.append(head + [SP.fmt_p(P[k]["p"]) for k in M])
    ci.append(head + ["CI low"] + [SP.fmt_signed(P[k]["ci_low"] / P[k]["mean_a"] * 100, 2) for k in M])
    ci.append(head + ["CI high"] + [SP.fmt_signed(P[k]["ci_high"] / P[k]["mean_a"] * 100, 2) for k in M])
w("table3_paired_delta_pct.csv", dl); w("table3b_paired_p_values.csv", pv); w("table3c_paired_ci95_pct.csv", ci)
rows = [["Policy", "Bots", "Seed"] + [SP.APA_HEAD[k] for k in M]]
for a in lm:
    for s in sorted(S["per_seed"][a], key=int): v = S["per_seed"][a][s]; rows.append(lm[a] + [int(s)] + [SP.fmt(v[k], SP.DECIMALS[k]) for k in M])
w("table4_per_seed.csv", rows)
rows = [["Policy", "Bots", "Seed", "Order batching time per decision (s)", "Order batching time total (s)", "Decisions"]]; agg = {}
for a in lm:
    for s in sorted(S["per_seed"][a], key=int):
        t = io.open(glob.glob(os.path.join(E.run_dir_of(eid, a, int(s)), "*", "statistics.txt"))[0], encoding="utf-8", errors="ignore").read()
        g = lambda k: float(re.search(r"^%s: ([-\d.E+]+)" % k, t, re.M).group(1))
        av, tot, n = g("StatTimingOrderBatchingAverage"), g("StatTimingOrderBatchingOverall"), g("StatTimingOrderBatchingCount")
        rows.append(lm[a] + [int(s), SP.fmt(av, 4), SP.fmt(tot, 1), int(n)]); agg.setdefault(a, []).append((av, tot, n))
w("table5_solve_time_per_seed.csv", rows)
w("table5_solve_time_summary.csv", [["Policy", "Bots", "Time per decision (s)", "Total time (s)", "Decisions"]] +
  [lm[a] + [SP.fmt(np.mean([x[0] for x in agg[a]]), 4), SP.fmt(np.mean([x[1] for x in agg[a]]), 1), SP.fmt(np.mean([x[2] for x in agg[a]]), 0)] for a in lm])
shutil.copy(os.path.join(d, "stats.xlsx"), os.path.join(out, "stats_full.xlsx")); shutil.copy(os.path.join(d, "apa_tables.docx"), os.path.join(out, "apa_tables.docx"))
print("bundle ->", out, "| excel:", S["excel_check"]["status"])
