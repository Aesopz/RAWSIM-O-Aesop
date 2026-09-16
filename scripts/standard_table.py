# -*- coding: utf-8 -*-
"""Standard 12-column data table plus scenario line (user rule, 2026-09-15).

Columns (fixed order):
  policy, bots, Items, Lines, Orders, Pile-on, Trips, Trips/Orders, m/Line,
  EOR(kJ/order), TurnoverMedian(s), StationIdle(%)

Scenario line is read from the run outputs and the xsett/xlayo files, never from directory
names (those are unreliable):
  2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock

Usage (as a module):
  from standard_table import print_table
  print_table([("M1G", ["p5_m1g_6_s0", ...]), ("M4G-NS", [...])],
              xsett=r"Material\\Instances\\CoreBenchmark\\small\\small_o100_mu100_2h_inv70.xsett",
              xlayo=r"Material\\Instances\\CoreBenchmark\\small\\small_6bot.xlayo")
"""
import csv, glob, os, re, statistics as st

ROOT = r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
OUT = os.path.join(ROOT, "out")
COLS = ["Items", "Lines", "Orders", "Pile-on", "Trips", "Trips/Orders", "m/Line",
        "EOR(kJ/order)", "TurnoverMedian(s)", "StationIdle(%)"]


def run_metrics(run_dir):
    base = glob.glob(os.path.join(OUT, run_dir, "*", "statistics.txt"))
    if not base:
        raise FileNotFoundError(run_dir)
    txt = open(base[0], encoding="utf-8", errors="ignore").read()
    g = lambda k: float(re.search(r"^" + re.escape(k) + r": ([-\d.E+]+)", txt, re.M).group(1))
    r = {"Items": g("StatOverallItemsHandled"), "Lines": g("StatOverallLinesHandled"),
         "Orders": g("StatOverallOrdersHandled"), "EOR(kJ/order)": g("KPI_EOR"),
         "TurnoverMedian(s)": g("StatMedianTurnoverTime")}
    r["m/Line"] = g("StatOverallDistanceTraveled") / r["Lines"]
    r["_hours"] = r["Orders"] / g("StatThroughputOrdersPerHour")
    run = os.path.dirname(base[0])
    for row in csv.reader(open(os.path.join(run, "kpi_report.csv"), encoding="utf-8")):
        if row[1] == "system_order_pile_on": r["Pile-on"] = float(row[4])
        if row[1] == "output_station_arrivals": r["Trips"] = float(row[4])
    r["Trips/Orders"] = r["Trips"] / r["Orders"]
    # StationIdle, complete version: output stations IdleTime/UpTime, start-up included
    r["StationIdle(%)"] = st.mean(
        float(x["IdleTime"]) / float(x["UpTime"]) * 100
        for x in csv.DictReader(open(os.path.join(run, "stationstatistics.csv"), encoding="utf-8"), delimiter=";")
        if x["Ident"].startswith("OutputStation"))
    fp = open(os.path.join(run, "footprint.csv"), encoding="utf-8").read().strip().splitlines()[-1].split(";")
    r["_bots"], r["_pods"], r["_istations"], r["_ostations"], r["_skus"] = fp[16], fp[17], fp[18], fp[19], fp[25]
    r["_controller"] = fp[10]
    return r


def scenario_line(runs, seeds_text, xsett, xlayo):
    """Scenario line from run outputs plus xsett/xlayo; raises if the runs mix scenarios."""
    xs = open(os.path.join(ROOT, xsett), encoding="utf-8").read()
    xl = open(os.path.join(ROOT, xlayo), encoding="utf-8").read()
    tag = lambda t, s: re.search("<%s>([^<]*)</%s>" % (t, t), s).group(1)
    cells = ((int(tag("NrHorizontalAisles", xl)) + 1) * (int(tag("NrVerticalAisles", xl)) + 1)
             * int(tag("HorizontalLengthBlock", xl)) * 2)
    hours = {round(x["_hours"], 2) for x in runs}
    meta = {k: {x[k] for x in runs} for k in ("_ostations", "_istations", "_skus", "_pods")}
    if len(hours) > 1 or any(len(v) > 1 for v in meta.values()):
        raise ValueError("runs mix scenarios: hours=%s meta=%s" % (hours, meta))
    return "%g h · %s seeds · %s Pstations · %s Rstations · %s SKUs · %s backlog · %s pods / %d cells · cap %s · %g%% stock" % (
        hours.pop(), seeds_text, meta["_ostations"].pop(), meta["_istations"].pop(), meta["_skus"].pop(),
        tag("OrderCount", xs), meta["_pods"].pop(), cells, tag("PodCapacity", xl),
        float(tag("InitialInventory", xs)) * 100)


def print_table(groups, xsett, xlayo):
    """groups: list of (policy_label, [run_dir, ...]). All groups must share one scenario
    (bots may differ). xsett/xlayo: files used by the runs (any bot-count variant of the layout)."""
    all_runs, rows = [], []
    for label, dirs in groups:
        R = [run_metrics(d) for d in dirs]
        all_runs.extend(R)
        bots = {x["_bots"] for x in R}
        rows.append((label, "/".join(sorted(bots, key=int)) + "b", R))
    seeds_text = "/".join(str(s) for s in sorted({len(R) for _, _, R in rows}))
    print(scenario_line(all_runs, seeds_text, xsett, xlayo))
    print("policy,bots," + ",".join(COLS))
    for label, bots, R in rows:
        print("%s,%s,%s" % (label, bots, ",".join("%.4g" % st.mean(x[c] for x in R) for c in COLS)))
