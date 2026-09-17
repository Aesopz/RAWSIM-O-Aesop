# -*- coding: utf-8 -*-
"""Reusable experiment runner and data manager (rules: docs/experiment-data-management.md).

Layout (<kind> = fast | dev | formal)
  experiments/registry.csv                        one row per run (global)
  experiments/<kind>/<id>/experiment.json         declaration (kind, question, motivation, arms, comparisons)
  experiments/<kind>/<id>/inputs/                 files this experiment changed (copied from Canon, one variable edited)
  experiments/<kind>/<id>/runs/<arm>_s<seed>/     engine output + config/ snapshot + manifest.json
  experiments/<kind>/<id>/launch/                 generated worker scripts
  experiments/<kind>/<id>/NOTES.md                why / settings / results (auto) + analysis (hand-written, preserved)
  experiments/<kind>/<id>/results.csv|verify.json
  Material/Instances/Canon/VERSION.json           canon version + fingerprint

Kinds: fast = 1 seed; dev = 1 seed (trend) or 5 seeds (statistics); formal = 10 seeds with declared comparisons.

Commands (run with PYTHONIOENCODING=utf-8)
  new        <kind> <id> "<question>"              create experiments/<kind>/<id>/ skeleton
  plan       <id>                                  validate; print scenario lines for user confirmation
  run        <id> [--workers N] [--max-sims M] [--resume]
                                                   snapshot configs, write manifests, start throttled workers
  wait       <id> [--poll S]                       block until all runs of <id> finished/failed; print each
  verify     <id>                                  cleanliness checks -> registry validity
  table      <id>                                  12-column table + declared paired tests -> results.csv
  status     [id]                                  registry summary
  canon-bump "<note>"                              bump Canon version, then stale-check
  stale-check                                      mark runs whose config differs from current Canon as superseded
  supersede  <id> "<reason>"                       manual supersede
  prune      <id> --yes                            delete engine output of superseded/invalid runs (keeps manifest+config)
  (internal) start <run_dir> <max_sims> / finalize <run_dir> <exit>

experiment.json
{
  "id": "2026-09-16_m4g_n2",
  "question": "...",
  "scenario_confirmed_by_user": false,
  "arms": [
    {"label": "m4g_6b",    "xlayo": "Canon/small/small_6b_2p_2r.xlayo",
     "xsett": "Canon/small/small_fill100_mu100_2h_inv70.xsett", "xconf": "Canon/small/m4g.xconf", "seeds": [0,1,2]},
    {"label": "m4g_n2_6b", "xlayo": "Canon/small/small_6b_2p_2r.xlayo",
     "xsett": "Canon/small/small_fill100_mu100_2h_inv70.xsett", "xconf": "inputs/m4g_n2.xconf",
     "templates": {"xconf": "Canon/small/m4g.xconf"}, "changed": ["MaxPartsPerOrder"], "seeds": [0,1,2]}
  ],
  "comparisons": [{"a": "m4g_6b", "b": "m4g_n2_6b", "kind": "ablation", "variable": "MaxPartsPerOrder"}]
}
Paths starting with "inputs/" are relative to the experiment folder; all others to Material/Instances.
"""
import csv, datetime, difflib, glob, hashlib, io, json, math, os, re, shutil, statistics as st, subprocess, sys, time

ROOT = r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
INST = os.path.join(ROOT, "Material", "Instances")
CANON = os.path.join(INST, "Canon")
CANON_VERSION = os.path.join(CANON, "VERSION.json")
EXP_ROOT = os.path.join(ROOT, "experiments")
REGISTRY = os.path.join(EXP_ROOT, "registry.csv")
CLI = os.path.join(ROOT, r"RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe")
DLL = os.path.join(ROOT, r"RAWSimO.CLI\bin\x64\Release\RAWSimO.Core.dll")
# Experiment classes (docs/experiment-data-management.md R0):
#   fast   quick check / smoke / guard            1 seed
#   dev    iterative development of a component   1 seed (trend) or 5 seeds (statistics)
#   formal thesis data: treatment + control       10 seeds, comparisons required
EXP_KINDS = {"fast": (1,), "dev": (1, 5), "formal": (10,)}
REG_FIELDS = ["run_id", "experiment", "kind", "arm", "seed", "bots", "xlayo", "xsett", "xconf", "xconf_sha", "canon_version",
              "dll_sha", "git_head", "git_dirty_hash", "queued", "started", "finished", "exit", "hours", "controller",
              "status", "validity", "note"]
NAME_TAG = {".xconf": "Name", ".xsett": "Name", ".xlayo": "NameLayout"}
KINDS = ("xlayo", "xsett", "xconf")
COLS = ["Items", "Lines", "Orders", "Pile-on", "Trips", "Trips/Orders", "m/Line",
        "EOR(kJ/order)", "TurnoverMedian(s)", "StationIdle(%)"]


# ================================================================ basic helpers
def now():
    return datetime.datetime.now().isoformat(timespec="seconds")

def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()

def read_text(path):
    return io.open(path, encoding="utf-8-sig", newline="").read().replace("\r\n", "\n")

def tag(text, name, default=None):
    m = re.search(r"<%s>([^<]*)</%s>" % (name, name), text)
    return m.group(1) if m else default

def exp_dir(exp_id):
    hits = [d for d in glob.glob(os.path.join(EXP_ROOT, "*", exp_id)) if os.path.isdir(d)]
    if len(hits) != 1:
        raise SystemExit("experiment %s: expected exactly one experiments/<kind>/%s folder, found %d" % (exp_id, exp_id, len(hits)))
    return hits[0]

def resolve(exp_id, rel):
    if rel.startswith("inputs/"):
        return os.path.join(exp_dir(exp_id), rel.replace("/", os.sep))
    return os.path.join(INST, rel.replace("/", os.sep))

def load_exp(exp_id):
    exp_id = os.path.basename(os.path.normpath(exp_id))
    d = exp_dir(exp_id)
    path = os.path.join(d, "experiment.json")
    if not os.path.exists(path):
        raise SystemExit("no experiment.json for %s" % exp_id)
    with io.open(path, encoding="utf-8") as f:
        exp = json.load(f)
    if exp["id"] != exp_id:
        raise SystemExit("experiment id %r must equal folder name %r" % (exp["id"], exp_id))
    folder_kind = os.path.basename(os.path.dirname(d))
    if exp.get("kind") != folder_kind:
        raise SystemExit("experiment kind %r must equal parent folder %r" % (exp.get("kind"), folder_kind))
    return exp

def run_dir_of(exp_id, arm_label, seed):
    return os.path.join(exp_dir(exp_id), "runs", "%s_s%d" % (arm_label, seed))

def run_id_of(exp_id, arm_label, seed):
    return "%s/%s_s%d" % (exp_id, arm_label, seed)

def xconf_flags(text):
    block = re.search(r"<OrderBatchingConfig[^>]*xsi:type=\"([^\"]+)\"[^>]*>(.*?)</OrderBatchingConfig>", text, re.S)
    if not block:
        return None, {}
    return block.group(1), dict(re.findall(r"<([A-Za-z0-9_]+)>([^<]*)</\1>", block.group(2)))

_GEN_CACHE = {}
def generator_sku_count(gen_name):
    if gen_name not in _GEN_CACHE:
        hits = glob.glob(os.path.join(ROOT, "Material", "Resources", gen_name)) or \
               glob.glob(os.path.join(ROOT, "Material", "**", gen_name), recursive=True)
        count = None
        if hits:
            block = re.search(r"<ItemDescriptions>(.*?)</ItemDescriptions>", read_text(hits[0]), re.S)
            count = len(re.findall(r"<Key>", block.group(1))) if block else None
        _GEN_CACHE[gen_name] = count
    return _GEN_CACHE[gen_name]

def scenario_from_files(xlayo_path, xsett_path):
    xl, xs = read_text(xlayo_path), read_text(xsett_path)
    cells = ((int(tag(xl, "NrHorizontalAisles")) + 1) * (int(tag(xl, "NrVerticalAisles")) + 1)
             * int(tag(xl, "HorizontalLengthBlock")) * 2)
    side = lambda kind: sum(int(tag(xl, "N%s%s" % (kind, d), "0")) for d in ("West", "East", "South", "North"))
    return {"hours": float(tag(xs, "SimulationDuration")) / 3600.0,
            "pstations": side("PickStation"), "rstations": side("ReplenishmentStation"),
            "skus": generator_sku_count(tag(xs, "GeneratorConfigFile")), "backlog": int(tag(xs, "OrderCount")),
            "pods": int(math.floor(float(tag(xl, "PodAmount")) * cells)), "cells": cells,
            "cap": tag(xl, "PodCapacity"), "stock": float(tag(xs, "InitialInventory")) * 100,
            "bots": int(tag(xl, "BotCount"))}

def scenario_line(sc, seeds):
    return "%g h · %s seeds · %d Pstations · %d Rstations · %s SKUs · %d backlog · %d pods / %d cells · cap %s · %g%% stock" % (
        sc["hours"], seeds, sc["pstations"], sc["rstations"], sc["skus"], sc["backlog"], sc["pods"], sc["cells"],
        sc["cap"], sc["stock"])

def git_state():
    try:
        head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT).decode().strip()
        diff = subprocess.check_output(["git", "diff", "HEAD", "--", "RAWSimO.Core", "RAWSimO.CLI"], cwd=ROOT)
        return head, hashlib.sha256(diff).hexdigest()[:16]
    except Exception as e:
        return "unknown", "unknown(%s)" % type(e).__name__

def running_sims():
    try:
        out = subprocess.check_output(["tasklist", "/fi", "imagename eq RAWSimO.CLI.exe"], creationflags=0x08000000).decode(errors="ignore")
        return out.count("RAWSimO.CLI.exe")
    except Exception:
        return 0

def meaningful_diff(a_text, b_text, allowed_tags):
    """Changed lines between two config texts, ignoring lines whose tag is in allowed_tags."""
    out = []
    for l in difflib.unified_diff(a_text.split("\n"), b_text.split("\n"), lineterm="", n=0):
        if l[:1] not in "+-" or l.startswith(("+++", "---")):
            continue
        t = re.search(r"<([A-Za-z0-9_]+)[ >/]", l)
        if t and t.group(1) in allowed_tags:
            continue
        if not l[1:].strip():
            continue
        out.append(l.strip())
    return out


# ================================================================ canon version
def canon_fingerprint():
    h = hashlib.sha256()
    for path in sorted(glob.glob(os.path.join(CANON, "**", "*.x*"), recursive=True)):
        h.update(os.path.relpath(path, CANON).replace("\\", "/").encode())
        h.update(read_text(path).encode("utf-8"))
    return h.hexdigest()[:16]

def canon_version():
    if not os.path.exists(CANON_VERSION):
        return {"version": 0, "fingerprint": None, "history": []}
    with io.open(CANON_VERSION, encoding="utf-8") as f:
        return json.load(f)

def current_canon_version_checked():
    v = canon_version()
    fp = canon_fingerprint()
    if v.get("fingerprint") != fp:
        raise SystemExit("Canon files changed without `exp.py canon-bump` (recorded %s, actual %s)" % (v.get("fingerprint"), fp))
    return v

def cmd_canon_bump(note):
    v = canon_version()
    fp = canon_fingerprint()
    new = {"version": v["version"] + 1, "fingerprint": fp, "date": now(), "note": note,
           "history": v.get("history", []) + [{"version": v["version"] + 1, "date": now(), "note": note, "fingerprint": fp}]}
    with io.open(CANON_VERSION, "w", encoding="utf-8") as f:
        json.dump(new, f, indent=1, ensure_ascii=False)
    print("Canon version %d -> %d (%s): %s" % (v["version"], new["version"], fp, note))
    cmd_stale_check()


# ================================================================ registry (locked, atomic)
def _lock():
    os.makedirs(EXP_ROOT, exist_ok=True)
    lock = REGISTRY + ".lock"
    for _ in range(1200):
        try:
            return os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY), lock
        except FileExistsError:
            time.sleep(0.05)
    raise SystemExit("registry lock timeout: " + lock)

def registry_rows():
    if not os.path.exists(REGISTRY):
        return []
    with io.open(REGISTRY, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))

def registry_upsert(updates):
    fd, lock = _lock()
    try:
        rows = registry_rows()
        index = {r["run_id"]: r for r in rows}
        for u in updates:
            if u["run_id"] in index:
                index[u["run_id"]].update({k: v for k, v in u.items() if v is not None})
            else:
                row = {k: "" for k in REG_FIELDS}
                row.update(u)
                rows.append(row)
                index[u["run_id"]] = row
        tmp = REGISTRY + ".tmp"
        with io.open(tmp, "w", encoding="utf-8", newline="") as f:
            w = csv.DictWriter(f, fieldnames=REG_FIELDS)
            w.writeheader()
            for r in rows:
                w.writerow({k: r.get(k, "") for k in REG_FIELDS})
        os.replace(tmp, REGISTRY)
    finally:
        os.close(fd)
        os.remove(lock)


# ================================================================ new / plan
def cmd_new(kind, exp_id, question):
    if kind not in EXP_KINDS:
        raise SystemExit("kind must be one of %s" % ", ".join(EXP_KINDS))
    if glob.glob(os.path.join(EXP_ROOT, "*", exp_id)):
        raise SystemExit("experiment id already used: " + exp_id)
    d = os.path.join(EXP_ROOT, kind, exp_id)
    os.makedirs(os.path.join(d, "inputs"))
    skeleton = {"id": exp_id, "kind": kind, "question": question, "motivation": "",
                "scenario_confirmed_by_user": False, "arms": [], "comparisons": []}
    with io.open(os.path.join(d, "experiment.json"), "w", encoding="utf-8") as f:
        json.dump(skeleton, f, indent=1, ensure_ascii=False)
    print("created " + d)

def plan(exp):
    problems, lines, labels, total = [], [], set(), 0
    lines.append("experiment: %s" % exp["id"])
    kind = exp.get("kind")
    lines.append("kind:       %s" % kind)
    lines.append("question:   %s" % exp.get("question", "(missing)"))
    lines.append("motivation: %s" % exp.get("motivation", "(missing)"))
    if kind not in EXP_KINDS:
        problems.append("kind must be one of %s" % ", ".join(EXP_KINDS))
    if not exp.get("question"):
        problems.append("question missing")
    if not exp.get("motivation"):
        problems.append("motivation missing (why this experiment is run)")
    if not exp.get("arms"):
        problems.append("no arms")
    if kind in EXP_KINDS:
        for arm in exp.get("arms", []):
            if len(arm["seeds"]) not in EXP_KINDS[kind]:
                problems.append("%s experiment: arm %s has %d seeds, allowed %s" % (kind, arm["label"], len(arm["seeds"]), EXP_KINDS[kind]))
        if len({len(a["seeds"]) for a in exp.get("arms", [])}) > 1:
            problems.append("all arms must use the same number of seeds")
    if kind == "formal":
        if not exp.get("comparisons"):
            problems.append("formal experiment needs comparisons (treatment vs control)")
        in_comp = {c[k] for c in exp.get("comparisons", []) for k in ("a", "b")}
        for arm in exp.get("arms", []):
            if arm["label"] not in in_comp:
                problems.append("formal experiment: arm %s is not in any comparison" % arm["label"])
        for c in exp.get("comparisons", []):
            if not c.get("variable"):
                problems.append("formal comparison %s vs %s must name its single differing variable" % (c["a"], c["b"]))
    for arm in exp.get("arms", []):
        if arm["label"] in labels:
            problems.append("duplicate arm label " + arm["label"])
        labels.add(arm["label"])
        if not re.match(r"^[A-Za-z0-9_.-]+$", arm["label"]):
            problems.append("arm label must be [A-Za-z0-9_.-]: " + arm["label"])
        ok = True
        for kind in KINDS:
            rel = arm[kind]
            path = resolve(exp["id"], rel)
            if not os.path.exists(path):
                problems.append("%s: missing %s" % (arm["label"], rel)); ok = False; continue
            base, ext = os.path.splitext(os.path.basename(rel))
            internal = tag(read_text(path), NAME_TAG[ext])
            if internal != base:
                problems.append("%s: %s internal name %r != file name %r" % (arm["label"], rel, internal, base))
            template = arm.get("templates", {}).get(kind)
            if template is None:
                if not rel.startswith("Canon/"):
                    problems.append("%s: %s is not in Canon/ and declares no template" % (arm["label"], rel))
                continue
            if not template.startswith("Canon/"):
                problems.append("%s: template %s must be in Canon/" % (arm["label"], template))
            allowed = set(arm.get("changed", [])) | {NAME_TAG[ext]}
            diff = meaningful_diff(read_text(resolve(exp["id"], template)), read_text(path), allowed)
            for l in diff:
                problems.append("%s: %s differs from %s outside declared variables: %s" % (arm["label"], rel, template, l))
            shown = meaningful_diff(read_text(resolve(exp["id"], template)), read_text(path), {NAME_TAG[ext]})
            if shown:
                lines.append("  %-20s %s vs %s: %s" % (arm["label"], kind, template, " | ".join(shown)))
        if ok:
            sc = scenario_from_files(resolve(exp["id"], arm["xlayo"]), resolve(exp["id"], arm["xsett"]))
            ob_type, _ = xconf_flags(read_text(resolve(exp["id"], arm["xconf"])))
            arm["_sc"] = sc
            lines.append("  %-20s bots %d · %s · %s" % (arm["label"], sc["bots"], os.path.basename(arm["xconf"]), ob_type))
            lines.append("  %-20s %s" % ("", scenario_line(sc, len(arm["seeds"]))))
        total += len(arm["seeds"])
    for comp in exp.get("comparisons", []):
        a = next((x for x in exp["arms"] if x["label"] == comp["a"]), None)
        b = next((x for x in exp["arms"] if x["label"] == comp["b"]), None)
        if a is None or b is None:
            problems.append("comparison references unknown arm: %s" % comp); continue
        if "_sc" in a and "_sc" in b:
            sa = {k: v for k, v in a["_sc"].items() if k != "bots"}
            sb = {k: v for k, v in b["_sc"].items() if k != "bots"}
            if sa != sb:
                problems.append("comparison %s vs %s mixes scenarios: %s / %s" % (comp["a"], comp["b"], sa, sb))
        if a["seeds"] != b["seeds"]:
            problems.append("comparison %s vs %s has different seed lists" % (comp["a"], comp["b"]))
    lines.append("runs: %d" % total)
    lines.append("canon version: %s" % canon_version().get("version"))
    lines.append("scenario_confirmed_by_user: %s" % exp.get("scenario_confirmed_by_user", False))
    return problems, lines

def cmd_plan(exp_id):
    exp = load_exp(exp_id)
    problems, lines = plan(exp)
    print("\n".join(lines))
    if problems:
        print("PROBLEMS:")
        for p in problems:
            print("  - " + p)
        sys.exit(1)
    print("PLAN OK")


# ================================================================ run / resume
def cmd_run(exp_id, workers=6, max_sims=6, resume=False):
    exp = load_exp(exp_id)
    if not exp.get("scenario_confirmed_by_user"):
        raise SystemExit("scenario_confirmed_by_user is not true: show `plan` output to the user first")
    problems, _ = plan(exp)
    if problems:
        raise SystemExit("plan has problems; run `exp.py plan %s`" % exp_id)
    cv = current_canon_version_checked()
    head, dirty = git_state()
    cli, dll = binaries_for(exp)
    dll_sha = sha256_file(dll)
    dll_built = datetime.datetime.fromtimestamp(os.path.getmtime(dll)).isoformat(timespec="seconds")
    reg = {r["run_id"]: r for r in registry_rows()}
    jobs = []
    for arm in exp["arms"]:
        if arm.get("reuse_from"):
            link_reused_arm(exp, arm, cv, dll_sha, head, dirty)
            continue
        for seed in arm["seeds"]:
            rid = run_id_of(exp_id, arm["label"], seed)
            rdir = run_dir_of(exp_id, arm["label"], seed)
            prev = reg.get(rid)
            if prev and not resume:
                raise SystemExit("run already registered: %s (use --resume, or a new experiment id)" % rid)
            if prev and resume and prev["status"] == "finished" and prev["exit"] == "0":
                continue
            if os.path.exists(rdir):
                for sub in os.listdir(rdir):                      # clear partial engine output from an earlier attempt
                    p = os.path.join(rdir, sub)
                    if sub not in ("config", "manifest.json"):
                        shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)
            cfg = os.path.join(rdir, "config")
            if os.path.exists(cfg):
                shutil.rmtree(cfg)
            os.makedirs(cfg)
            snap = {}
            for kind in KINDS:
                src = resolve(exp_id, arm[kind])
                dst = os.path.join(cfg, os.path.basename(src))
                shutil.copyfile(src, dst)
                snap[kind] = {"source": arm[kind], "template": arm.get("templates", {}).get(kind),
                              "snapshot": os.path.relpath(dst, ROOT), "sha256": sha256_file(dst)}
            ob_type, flags = xconf_flags(read_text(os.path.join(cfg, os.path.basename(arm["xconf"]))))
            sc = scenario_from_files(resolve(exp_id, arm["xlayo"]), resolve(exp_id, arm["xsett"]))
            args = [cli, snap["xlayo"]["snapshot"], snap["xsett"]["snapshot"], snap["xconf"]["snapshot"],
                    os.path.relpath(rdir, ROOT), str(seed)]
            manifest = {"run_id": rid, "experiment": exp_id, "kind": exp["kind"], "arm": arm["label"], "seed": seed,
                        "question": exp.get("question"), "motivation": exp.get("motivation"), "changed": arm.get("changed", []), "config": snap,
                        "order_batching_type": ob_type, "order_batching_flags": flags, "scenario": sc,
                        "canon": {"version": cv["version"], "fingerprint": cv["fingerprint"]},
                        "dll": {"path": os.path.relpath(dll, ROOT), "sha256": dll_sha, "built": dll_built},
                        "git": {"head": head, "uncommitted_code_diff_sha": dirty},
                        "command": args, "queued": now(), "started": None, "finished": None, "exit": None,
                        "attempt": (json.load(io.open(os.path.join(rdir, "manifest.json"), encoding="utf-8")).get("attempt", 1) + 1)
                                   if os.path.exists(os.path.join(rdir, "manifest.json")) else 1}
            with io.open(os.path.join(rdir, "manifest.json"), "w", encoding="utf-8") as f:
                json.dump(manifest, f, indent=1, ensure_ascii=False)
            registry_upsert([{"run_id": rid, "experiment": exp_id, "kind": exp["kind"], "arm": arm["label"], "seed": str(seed),
                              "bots": str(sc["bots"]), "xlayo": arm["xlayo"], "xsett": arm["xsett"], "xconf": arm["xconf"],
                              "xconf_sha": snap["xconf"]["sha256"][:16], "canon_version": str(cv["version"]),
                              "dll_sha": dll_sha[:16], "git_head": head[:12], "git_dirty_hash": dirty,
                              "queued": manifest["queued"], "started": "", "finished": "", "exit": "", "hours": "",
                              "controller": "", "status": "queued", "validity": "unverified", "note": ""}])
            jobs.append((rdir, args))
    if not jobs:
        print("nothing to run")
        return
    launch = os.path.join(exp_dir(exp_id), "launch")
    if os.path.exists(launch):
        shutil.rmtree(launch)
    os.makedirs(launch)
    workers = max(1, min(int(workers), len(jobs)))
    py = sys.executable
    for k in range(workers):
        lines = ["@echo off", "setlocal enabledelayedexpansion", "cd /d " + ROOT, "set PYTHONIOENCODING=utf-8"]
        for i, (rdir, args) in enumerate(jobs):
            if i % workers != k:
                continue
            rel = os.path.relpath(rdir, ROOT)
            lines.append('"%s" scripts\\exp.py start "%s" %d' % (py, rel, int(max_sims)))
            lines.append(" ".join('"%s"' % a for a in args) + ' > "%s\\engine.log" 2>&1' % rel)
            lines.append('"%s" scripts\\exp.py finalize "%s" !errorlevel!' % (py, rel))
        lines.append('echo worker %d done %%date%% %%time%% >> "%s"' % (k, os.path.join(launch, "workers_done.txt")))
        io.open(os.path.join(launch, "worker_%d.cmd" % k), "w", encoding="utf-8", newline="").write("\r\n".join(lines) + "\r\n")
    for k in range(workers):
        subprocess.Popen(["cmd", "/c", os.path.join(launch, "worker_%d.cmd" % k)], cwd=ROOT, creationflags=0x08000000)
    print("launched %d runs on %d workers (global max %d simulations). progress: exp.py wait %s"
          % (len(jobs), workers, int(max_sims), exp_id))

def link_reused_arm(exp, arm, cv, dll_sha, head, dirty):
    """Reference arm whose runs already exist (reuse_from = {"experiment": id, "arm": label}
    or {"legacy": {seed: out/<dir>}}). Copies the finished output into this experiment's runs/
    so the folder is self-contained; verify/table then treat it like any other arm."""
    exp_id = exp["id"]
    src = arm["reuse_from"]
    reg = {r["run_id"]: r for r in registry_rows()}
    rows = []
    for seed in arm["seeds"]:
        rid = run_id_of(exp_id, arm["label"], seed)
        if rid in reg:
            continue
        rdir = run_dir_of(exp_id, arm["label"], seed)
        if "experiment" in src:
            sdir = run_dir_of(src["experiment"], src["arm"], seed)
            srow = reg.get(run_id_of(src["experiment"], src["arm"], seed))
            if srow is None or srow["validity"] != "valid":
                raise SystemExit("reuse_from %s/%s seed %d is not a valid registered run" % (src["experiment"], src["arm"], seed))
        else:
            sdir = os.path.join(ROOT, src["legacy"][str(seed)])
        if len(glob.glob(os.path.join(sdir, "*", "statistics.txt"))) != 1:
            raise SystemExit("reuse source has no finished run: " + sdir)
        shutil.copytree(sdir, rdir)
        mp = os.path.join(rdir, "manifest.json")
        if os.path.exists(mp):
            m = json.load(io.open(mp, encoding="utf-8"))
        else:                                                    # legacy out/ run: build a minimal manifest
            cfg = os.path.join(rdir, "config"); os.makedirs(cfg, exist_ok=True); snap = {}
            for kind in KINDS:
                p = resolve(exp_id, arm[kind]); dst = os.path.join(cfg, os.path.basename(p)); shutil.copyfile(p, dst)
                snap[kind] = {"source": arm[kind], "template": None, "snapshot": os.path.relpath(dst, ROOT), "sha256": sha256_file(dst)}
            ob, flags = xconf_flags(read_text(os.path.join(cfg, os.path.basename(arm["xconf"]))))
            m = {"config": snap, "order_batching_type": ob, "order_batching_flags": flags,
                 "dll": {"sha256": dll_sha, "note": "reused legacy run; DLL equivalence asserted by experiment.json reuse_from"},
                 "git": {"head": head, "uncommitted_code_diff_sha": dirty}, "exit": 0,
                 "finished": datetime.datetime.fromtimestamp(os.path.getmtime(glob.glob(os.path.join(rdir, "*", "statistics.txt"))[0])).isoformat(timespec="seconds")}
        sc = scenario_from_files(resolve(exp_id, arm["xlayo"]), resolve(exp_id, arm["xsett"]))
        m.update(run_id=rid, experiment=exp_id, kind=exp["kind"], arm=arm["label"], seed=seed, question=exp.get("question"),
                 motivation=exp.get("motivation"), changed=arm.get("changed", []), scenario=sc,
                 canon={"version": cv["version"], "fingerprint": cv["fingerprint"]}, retro={"reused_from": src, "linked": now()})
        m["config"] = {k: dict(v, source=arm[k], template=arm.get("templates", {}).get(k)) for k, v in m["config"].items()}
        for k in KINDS:                                          # snapshot path now lives in this run dir
            m["config"][k]["snapshot"] = os.path.relpath(os.path.join(rdir, "config", os.path.basename(m["config"][k]["snapshot"])), ROOT)
        json.dump(m, io.open(mp, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
        rows.append({"run_id": rid, "experiment": exp_id, "kind": exp["kind"], "arm": arm["label"], "seed": str(seed), "bots": str(sc["bots"]),
                     "xlayo": arm["xlayo"], "xsett": arm["xsett"], "xconf": arm["xconf"], "xconf_sha": m["config"]["xconf"]["sha256"][:16],
                     "canon_version": str(cv["version"]), "dll_sha": m["dll"]["sha256"][:16], "git_head": str(m["git"]["head"])[:12],
                     "git_dirty_hash": str(m["git"]["uncommitted_code_diff_sha"]), "queued": "", "started": "", "finished": m.get("finished", ""),
                     "exit": "0", "hours": "", "controller": "", "status": "finished", "validity": "unverified",
                     # inherit a canon-equivalence proof from the source row so stale-check keeps honouring it
                     "note": "reused from " + json.dumps(src, ensure_ascii=False)
                             + (("; " + srow["note"]) if "experiment" in src and CANON_EQUIV_TAG in (srow.get("note") or "") else "")})
    if rows:
        registry_upsert(rows)
        print("linked %d reused runs for arm %s" % (len(rows), arm["label"]))

def binaries_for(exp):
    """experiment.json may name an alternate build folder ("binary": "bin_xxx") so a flag can be
    tested while Release is locked by a running batch. The DLL SHA is recorded per run either way."""
    b = exp.get("binary")
    if not b:
        return CLI, DLL
    d = os.path.join(ROOT, b)
    return os.path.join(d, "RAWSimO.CLI.exe"), os.path.join(d, "RAWSimO.Core.dll")

def _manifest_path(rdir):
    return os.path.join(rdir if os.path.isabs(rdir) else os.path.join(ROOT, rdir), "manifest.json")

def _update_manifest(rdir, **kv):
    path = _manifest_path(rdir)
    with io.open(path, encoding="utf-8") as f:
        m = json.load(f)
    m.update(kv)
    with io.open(path, "w", encoding="utf-8") as f:
        json.dump(m, f, indent=1, ensure_ascii=False)
    return m

def cmd_start(rdir, max_sims):
    while running_sims() >= int(max_sims):
        time.sleep(30)
    mp = _manifest_path(rdir); dll_path = os.path.join(ROOT, json.load(io.open(mp, encoding="utf-8"))["dll"]["path"])
    m = _update_manifest(rdir, started=now(), dll_at_start_sha256=sha256_file(dll_path))
    registry_upsert([{"run_id": m["run_id"], "started": m["started"], "status": "running"}])

def cmd_finalize(rdir, exit_code):
    m = _update_manifest(rdir, finished=now(), exit=int(exit_code))
    registry_upsert([{"run_id": m["run_id"], "finished": m["finished"], "exit": str(exit_code),
                      "status": "finished" if int(exit_code) == 0 else "failed"}])

def cmd_wait(exp_id, poll=60):
    exp = load_exp(exp_id)
    want = {run_id_of(exp_id, a["label"], s) for a in exp["arms"] for s in a["seeds"]}
    seen = set()
    while True:
        rows = {r["run_id"]: r for r in registry_rows() if r["run_id"] in want}
        for rid, r in sorted(rows.items()):
            if r["status"] in ("finished", "failed") and rid not in seen:
                seen.add(rid)
                print("%s %s exit=%s (%d/%d)" % (r["finished"], rid, r["exit"], len(seen), len(want)), flush=True)
        if len(seen) >= len(want):
            failed = sum(1 for r in rows.values() if r["status"] == "failed")
            print("ALL DONE %s: %d runs, %d failed" % (exp_id, len(want), failed), flush=True)
            return
        time.sleep(int(poll))


# ================================================================ retro import (runs launched before exp.py existed)
def cmd_import_retro(spec_path):
    """Move legacy out/<dir> runs into a formal experiment folder with manifests and registry rows.

    spec = {"experiment": {id, kind, question, motivation, arms: [...], comparisons: [...]},
            "evidence": "why the configs/DLL are known",
            "dll_sha256": "...", "launch_scripts": [...],
            "runs": {"<arm>": {"used": {"xlayo": path, "xsett": path, "xconf": path},
                               "legacy": {"<seed>": "out/<dir>"}}}}
    Each arm's experiment.json files must be honest Canon/inputs equivalents of the files actually
    used; this is checked line by line (name and declared `changed` tags excepted) before anything moves.
    """
    with io.open(spec_path, encoding="utf-8") as f:
        spec = json.load(f)
    exp = spec["experiment"]
    exp_id, kind = exp["id"], exp["kind"]
    if glob.glob(os.path.join(EXP_ROOT, "*", exp_id)):
        raise SystemExit("experiment id already used: " + exp_id)
    d = os.path.join(EXP_ROOT, kind, exp_id)
    os.makedirs(os.path.join(d, "inputs"))
    for name, text in spec.get("inputs", {}).items():
        io.open(os.path.join(d, "inputs", name), "w", encoding="utf-8", newline="").write(text)
    exp["scenario_confirmed_by_user"] = True
    exp["retro"] = {"evidence": spec["evidence"], "launch_scripts": spec["launch_scripts"]}
    with io.open(os.path.join(d, "experiment.json"), "w", encoding="utf-8") as f:
        json.dump(exp, f, indent=1, ensure_ascii=False)
    problems, _ = plan(json.loads(json.dumps(exp)))
    arms = {a["label"]: a for a in exp["arms"]}
    for label, r in spec["runs"].items():
        arm = arms[label]
        for kind_ in KINDS:
            ref = arm.get("templates", {}).get(kind_) or arm[kind_]
            ext = os.path.splitext(ref)[1]
            diff = meaningful_diff(read_text(resolve(exp_id, ref)), read_text(os.path.join(ROOT, r["used"][kind_])),
                                   {NAME_TAG[ext]} | set(arm.get("changed", [])))
            problems += ["%s: used %s differs from %s: %s" % (label, r["used"][kind_], ref, x) for x in diff]
        for seed in arm["seeds"]:
            src = os.path.join(ROOT, r["legacy"][str(seed)])
            if len(glob.glob(os.path.join(src, "*", "statistics.txt"))) != 1:
                problems.append("%s seed %d: %s does not hold exactly one finished run" % (label, seed, src))
    if problems:
        shutil.rmtree(d)
        raise SystemExit("import refused:\n  " + "\n  ".join(problems))
    cv = current_canon_version_checked()
    rows = []
    for label, r in spec["runs"].items():
        arm = arms[label]
        sc = scenario_from_files(resolve(exp_id, arm["xlayo"]), resolve(exp_id, arm["xsett"]))
        for seed in arm["seeds"]:
            src = os.path.join(ROOT, r["legacy"][str(seed)])
            rdir = run_dir_of(exp_id, label, seed)
            os.makedirs(os.path.join(rdir, "config"))
            snap = {}
            for kind_ in KINDS:
                used = os.path.join(ROOT, r["used"][kind_])
                dst = os.path.join(rdir, "config", os.path.basename(used))
                shutil.copyfile(used, dst)
                snap[kind_] = {"source": arm[kind_], "template": arm.get("templates", {}).get(kind_),
                               "actually_used": r["used"][kind_], "snapshot": os.path.relpath(dst, ROOT),
                               "sha256": sha256_file(dst)}
            for item in os.listdir(src):
                shutil.move(os.path.join(src, item), os.path.join(rdir, item))
            os.rmdir(src)
            stats = glob.glob(os.path.join(rdir, "*", "statistics.txt"))[0]
            fin = datetime.datetime.fromtimestamp(os.path.getmtime(stats)).isoformat(timespec="seconds")
            ob_type, flags = xconf_flags(read_text(os.path.join(rdir, "config", os.path.basename(r["used"]["xconf"]))))
            m = {"run_id": run_id_of(exp_id, label, seed), "experiment": exp_id, "kind": kind, "arm": label, "seed": seed,
                 "question": exp["question"], "motivation": exp["motivation"], "changed": arm.get("changed", []),
                 "config": snap, "order_batching_type": ob_type, "order_batching_flags": flags, "scenario": sc,
                 "canon": {"version": cv["version"], "fingerprint": cv["fingerprint"]},
                 "dll": {"sha256": spec["dll_sha256"], "note": "retro: see experiment.json retro.evidence"},
                 "git": {"head": "retro", "uncommitted_code_diff_sha": "retro"},
                 "retro": {"legacy_dir": r["legacy"][str(seed)], "imported": now()},
                 "queued": None, "started": None, "finished": fin, "exit": 0, "attempt": 1}
            with io.open(os.path.join(rdir, "manifest.json"), "w", encoding="utf-8") as f:
                json.dump(m, f, indent=1, ensure_ascii=False)
            rows.append({"run_id": m["run_id"], "experiment": exp_id, "kind": kind, "arm": label, "seed": str(seed),
                         "bots": str(sc["bots"]), "xlayo": arm["xlayo"], "xsett": arm["xsett"], "xconf": arm["xconf"],
                         "xconf_sha": snap["xconf"]["sha256"][:16], "canon_version": str(cv["version"]),
                         "dll_sha": spec["dll_sha256"][:16], "git_head": "retro", "git_dirty_hash": "retro",
                         "queued": "", "started": "", "finished": fin, "exit": "0", "hours": "", "controller": "",
                         "status": "finished", "validity": "unverified", "note": "retro import from " + r["legacy"][str(seed)]})
    registry_upsert(rows)
    print("imported %d runs into %s" % (len(rows), os.path.relpath(d, ROOT)))


# ================================================================ verify
def cmd_verify(exp_id):
    exp = load_exp(exp_id)
    results, dll_shas, arm_ctrl = [], set(), {}
    for arm in exp["arms"]:
        for seed in arm["seeds"]:
            rdir = run_dir_of(exp_id, arm["label"], seed)
            mp = os.path.join(rdir, "manifest.json")
            if not os.path.exists(mp):
                results.append(({"run_id": run_id_of(exp_id, arm["label"], seed), "arm": arm["label"]}, ["never launched"], None, None))
                continue
            with io.open(mp, encoding="utf-8") as f:
                m = json.load(f)
            fails, hours, controller = [], None, None
            if m.get("exit") != 0:
                fails.append("exit=%s" % m.get("exit"))
            stats = glob.glob(os.path.join(rdir, "*", "statistics.txt"))
            if not stats:
                fails.append("no statistics.txt")
            else:
                txt = read_text(stats[0])
                g = lambda k: float(re.search(r"^" + k + r": ([-\d.E+]+)", txt, re.M).group(1))
                hours = g("StatOverallOrdersHandled") / g("StatThroughputOrdersPerHour")
                if abs(hours - m["scenario"]["hours"]) > 0.01:
                    fails.append("hours %.3f != %.3f" % (hours, m["scenario"]["hours"]))
                fp = read_text(glob.glob(os.path.join(rdir, "*", "footprint.csv"))[0]).strip().split("\n")[-1].split(";")
                controller = fp[10]
                observed = {"bots": int(fp[16]), "pods": int(fp[17]), "rstations": int(fp[18]),
                            "pstations": int(fp[19]), "skus": int(fp[25])}
                for k, v in observed.items():
                    if m["scenario"].get(k) is not None and v != m["scenario"][k]:
                        fails.append("%s: footprint %s != expected %s" % (k, v, m["scenario"][k]))
                arm_ctrl.setdefault(m["arm"], set()).add(controller)
                engine_dir = os.path.basename(os.path.dirname(stats[0]))
                names = [os.path.splitext(os.path.basename(m["config"][k]["snapshot"]))[0] for k in KINDS]
                if not m.get("retro") and not engine_dir.startswith("-".join(names)):
                    fails.append("engine output folder %r does not read as %s (dishonest internal name?)" % (engine_dir, "-".join(names)))
            for kind, s in m["config"].items():
                if sha256_file(os.path.join(ROOT, s["snapshot"])) != s["sha256"]:
                    fails.append("%s snapshot changed after launch" % kind)
            at_start = m.get("dll_at_start_sha256")
            if at_start and at_start != m["dll"]["sha256"]:
                fails.append("DLL changed between queue and start")
            dll_shas.add(at_start or m["dll"]["sha256"])
            results.append((m, fails, hours, controller))
    for arm, ctrls in arm_ctrl.items():
        if len(ctrls) > 1:
            for m, fails, _, _ in results:
                if m.get("arm") == arm:
                    fails.append("controller differs across seeds: %s" % sorted(ctrls))
    if len(dll_shas) > 1:
        # experiment.json may declare builds proven bit-identical by a guard run:
        # "equivalent_dlls": {"guard": "<fast experiment id>", "sha256": ["...", "..."]}
        eq = exp.get("equivalent_dlls", {})
        allowed = set(x[:16] for x in eq.get("sha256", []))
        if allowed and all(x[:16] in allowed for x in dll_shas) and eq.get("guard"):
            print("note: %d DLL builds, declared equivalent by guard %s" % (len(dll_shas), eq["guard"]))
        else:
            for m, fails, _, _ in results:
                fails.append("experiment ran on %d different DLL builds" % len(dll_shas))
    updates, report = [], []
    prior_notes = {r["run_id"]: r["note"] for r in registry_rows()}
    for m, fails, hours, controller in results:
        validity = "valid" if not fails else "invalid"
        report.append({"run_id": m["run_id"], "validity": validity, "problems": fails})
        if "never launched" not in fails:
            # keep a canon-equivalence proof (or any reuse provenance) that earlier steps recorded
            keep = prior_notes.get(m["run_id"], "")
            keep = keep if (CANON_EQUIV_TAG in keep or keep.startswith("reused from")) else ""
            note = "; ".join([x for x in [keep, "; ".join(fails)] if x])
            updates.append({"run_id": m["run_id"], "hours": "%.3f" % hours if hours else "", "controller": controller or "",
                            "validity": validity, "note": note})
        print("%-55s %s %s" % (m["run_id"], validity, "; ".join(fails)))
    if updates:
        registry_upsert(updates)
    with io.open(os.path.join(exp_dir(exp_id), "verify.json"), "w", encoding="utf-8") as f:
        json.dump({"verified": now(), "canon_version": canon_version().get("version"), "runs": report}, f, indent=1, ensure_ascii=False)
    print("valid %d / %d" % (sum(1 for r in report if r["validity"] == "valid"), len(report)))
    cmd_stale_check(only=exp_id)


# ================================================================ table
def run_metrics(rdir):
    stats = glob.glob(os.path.join(rdir, "*", "statistics.txt"))[0]
    txt = read_text(stats)
    g = lambda k: float(re.search(r"^" + re.escape(k) + r": ([-\d.E+]+)", txt, re.M).group(1))
    r = {"Items": g("StatOverallItemsHandled"), "Lines": g("StatOverallLinesHandled"),
         "Orders": g("StatOverallOrdersHandled"), "EOR(kJ/order)": g("KPI_EOR"),
         "TurnoverMedian(s)": g("StatMedianTurnoverTime")}
    r["m/Line"] = g("StatOverallDistanceTraveled") / r["Lines"]
    sub = os.path.dirname(stats)
    for row in csv.reader(io.open(os.path.join(sub, "kpi_report.csv"), encoding="utf-8")):
        if row[1] == "system_order_pile_on": r["Pile-on"] = float(row[4])
        if row[1] == "output_station_arrivals": r["Trips"] = float(row[4])
    r["Trips/Orders"] = r["Trips"] / r["Orders"]
    r["StationIdle(%)"] = st.mean(float(x["IdleTime"]) / float(x["UpTime"]) * 100
                                  for x in csv.DictReader(io.open(os.path.join(sub, "stationstatistics.csv"), encoding="utf-8"), delimiter=";")
                                  if x["Ident"].startswith("OutputStation"))
    return r

def scenario_note_en(sc, seeds):
    return ("%g h simulated, %d seeds, %d pick and %d replenishment stations, %s SKUs, backlog of %d orders, "
            "%d pods in %d storage cells, pod capacity %s, %g%% initial stock." % (
                sc["hours"], seeds, sc["pstations"], sc["rstations"], sc["skus"], sc["backlog"], sc["pods"],
                sc["cells"], sc["cap"], sc["stock"]))

def cmd_table(exp_id):
    """All statistics come from scripts/stats_pipeline.py (scipy + Excel cross-check); nothing computed by hand."""
    import stats_pipeline as SP
    exp = load_exp(exp_id)
    reg = {r["run_id"]: r for r in registry_rows()}
    stale, arms, scen = set(), [], {}
    for arm in exp["arms"]:
        dirs = {}
        for seed in arm["seeds"]:
            rid = run_id_of(exp_id, arm["label"], seed)
            v = reg.get(rid, {}).get("validity")
            if v not in ("valid", "superseded"):
                raise SystemExit("run not verified: %s (validity=%s; run `verify` first)" % (rid, v))
            if v == "superseded":
                stale.add(arm["label"])
            dirs[seed] = run_dir_of(exp_id, arm["label"], seed)
        sc = scenario_from_files(resolve(exp_id, arm["xlayo"]), resolve(exp_id, arm["xsett"]))
        scen[arm["label"]] = sc
        arms.append({"label": arm["label"], "bots": sc["bots"], "seeds": list(arm["seeds"]), "dirs": dirs})
    sc0 = scen[arms[0]["label"]]
    same = all({k: v for k, v in s.items() if k != "bots"} == {k: v for k, v in sc0.items() if k != "bots"} for s in scen.values())
    comps = exp.get("comparisons", [])
    res = SP.run_pipeline(exp_dir(exp_id), exp.get("title_en") or exp["question"], scenario_note_en(sc0, len(arms[0]["seeds"])), arms, comps)
    text = SP.summary_text(arms, comps, res, scenario_line(sc0, len(arms[0]["seeds"])))
    if not same:
        text = "WARNING: arms differ in scenario beyond bots; scenario line shows the first arm\n" + text
    if stale:
        text = "WARNING: superseded (old canon) data in arms: %s\n" % ", ".join(sorted(stale)) + text
    io.open(os.path.join(exp_dir(exp_id), "results.csv"), "w", encoding="utf-8").write(text)
    print(text)
    print("outputs: stats.xlsx (Excel-verified), stats.json, apa_tables.docx")
    # (REPORTING-STANDARD 5.5) The CSV bundle is produced on EVERY table run, into the experiment
    # folder's csv/ subdir. Policy display names come from arm["display"] / arm["display_bots"] when
    # given, else from the label with a Legacy->M4G-WS style cleanup left to the author.
    label_map = {a["label"]: [a.get("display", a["label"]), scen[a["label"]]["bots"]] for a in exp["arms"]}
    import csv_bundle as CB
    CB.bundle(exp_id, os.path.join(exp_dir(exp_id), "csv"), label_map)
    write_notes(exp, text)


# ================================================================ notes (one light file per batch)
NOTES_ANALYSIS_MARK = "## 分析（Claude 當下判讀）"
NOTES_PLACEHOLDER = "（待填：這批數據說明了什麼、可信度、下一步）"

def write_notes(exp, results_text):
    """NOTES.md = auto-generated why/settings/results + a hand-written analysis section that is preserved."""
    exp_id = exp["id"]
    path = os.path.join(exp_dir(exp_id), "NOTES.md")
    analysis = NOTES_PLACEHOLDER
    if os.path.exists(path):
        old = read_text(path)
        if NOTES_ANALYSIS_MARK in old:
            analysis = old.split(NOTES_ANALYSIS_MARK, 1)[1].strip() or NOTES_PLACEHOLDER
    reg = [r for r in registry_rows() if r["experiment"] == exp_id]
    manifests = {}
    for arm in exp["arms"]:
        mp = os.path.join(run_dir_of(exp_id, arm["label"], arm["seeds"][0]), "manifest.json")
        if os.path.exists(mp):
            manifests[arm["label"]] = json.load(io.open(mp, encoding="utf-8"))
    any_m = next(iter(manifests.values()), {})
    seeds_n = len(exp["arms"][0]["seeds"]) if exp["arms"] else 0
    stage = {"fast": "快速驗證（1 seed）", "formal": "正式實驗（10 seeds）"}.get(
        exp["kind"], "迭代開發・看趨勢（1 seed）" if seeds_n == 1 else "迭代開發・看統計（5 seeds）")
    out = ["# %s" % exp_id, "",
           "- 類別：%s" % stage,
           "- 產生：%s・正典 v%s・DLL %s・git %s" % (now(), any_m.get("canon", {}).get("version", "?"),
                                                  any_m.get("dll", {}).get("sha256", "?")[:12], any_m.get("git", {}).get("head", "?")[:10]),
           "- 場次狀態：%s" % ", ".join("%s=%d" % (k, sum(1 for r in reg if r["validity"] == k))
                                   for k in sorted({r["validity"] for r in reg})),
           "", "## 為何做這組實驗", "", "**問題**：%s" % exp["question"], "", "**動機**：%s" % exp.get("motivation", ""),
           "", "## 設定", ""]
    for arm in exp["arms"]:
        sc = scenario_from_files(resolve(exp_id, arm["xlayo"]), resolve(exp_id, arm["xsett"]))
        out.append("- **%s**：%s・%s・seeds %s" % (arm["label"], os.path.basename(arm["xconf"]),
                                               manifests.get(arm["label"], {}).get("order_batching_type", "?"), arm["seeds"]))
        out.append("  - %d bots・%s" % (sc["bots"], scenario_line(sc, len(arm["seeds"]))))
        for kind in KINDS:
            tmpl = arm.get("templates", {}).get(kind)
            if tmpl:
                d = meaningful_diff(read_text(resolve(exp_id, tmpl)), read_text(resolve(exp_id, arm[kind])),
                                    {NAME_TAG[os.path.splitext(tmpl)[1]]})
                out.append("  - 相對 `%s`：%s" % (tmpl, "；".join("`%s`" % x for x in d) or "無差異"))
    if exp.get("comparisons"):
        out += ["", "比較："]
        out += ["- %s vs %s（%s；變數 %s）" % (c["b"], c["a"], c.get("kind", ""), c.get("variable", "")) for c in exp["comparisons"]]
    out += ["", "## 結果", "", "```", results_text.strip(), "```", "", NOTES_ANALYSIS_MARK, "", analysis, ""]
    io.open(path, "w", encoding="utf-8").write("\n".join(out))
    print("notes: " + os.path.relpath(path, ROOT) + ("  ← 分析段待填" if analysis == NOTES_PLACEHOLDER else ""))


# ================================================================ staleness
CANON_EQUIV_TAG = "canon-equivalent"   # registry note token: 'canon-equivalent(v2, guard <exp>)'
def cmd_stale_check(only=None):
    """A run is superseded when its config snapshot differs from the CURRENT Canon anywhere
    other than the internal name and the variables the experiment declared it changed."""
    cv = current_canon_version_checked()
    rows = registry_rows()
    updates, by_exp = [], {}
    for r in rows:
        if only and r["experiment"] != only:
            continue
        if r["validity"] not in ("valid", "unverified"):
            continue
        # A run proven bit-identical under the current Canon (guard experiment named in the note)
        # is exempt: its snapshot legitimately predates the Canon bump.
        if CANON_EQUIV_TAG in r["note"]:
            continue
        by_exp.setdefault(r["experiment"], []).append(r)
    for exp_id, exp_rows in sorted(by_exp.items()):
        try:
            exp = load_exp(exp_id)
        except SystemExit:
            continue
        arms = {a["label"]: a for a in exp["arms"]}
        cache = {}
        for r in exp_rows:
            arm = arms.get(r["arm"])
            mp = os.path.join(run_dir_of(exp_id, r["arm"], int(r["seed"])), "manifest.json")
            if arm is None or not os.path.exists(mp):
                continue
            if r["arm"] not in cache:
                with io.open(mp, encoding="utf-8") as f:
                    m = json.load(f)
                diffs = []
                for kind in KINDS:
                    s = m["config"][kind]
                    ref = s.get("template") or (s["source"] if s["source"].startswith("Canon/") else None)
                    if ref is None:
                        diffs.append("%s: no Canon reference" % kind); continue
                    ref_path = resolve(exp_id, ref)
                    if not os.path.exists(ref_path):
                        diffs.append("%s: Canon file %s no longer exists" % (kind, ref)); continue
                    ext = os.path.splitext(ref)[1]
                    allowed = {NAME_TAG[ext]} | (set(arm.get("changed", [])) if s.get("template") else set())
                    d = meaningful_diff(read_text(os.path.join(ROOT, s["snapshot"])), read_text(ref_path), allowed)
                    diffs.extend("%s %s" % (os.path.basename(ref), x) for x in d)
                cache[r["arm"]] = diffs
            diffs = cache[r["arm"]]
            if diffs:
                note = "superseded by Canon v%s: %s" % (cv["version"], " | ".join(diffs[:6]) + (" ..." if len(diffs) > 6 else ""))
                updates.append({"run_id": r["run_id"], "validity": "superseded",
                                "note": (r["note"] + "; " if r["note"] else "") + note})
    if updates:
        registry_upsert(updates)
    touched = sorted({u["run_id"].split("/")[0] for u in updates})
    print("stale-check (Canon v%s): %d runs superseded%s" % (cv["version"], len(updates),
                                                             (" in " + ", ".join(touched)) if touched else ""))
    if updates:
        print("figures built from these runs may now be stale: run  python scripts/fig_stale_check.py")

def cmd_supersede(exp_id, reason):
    rows = [r for r in registry_rows() if r["experiment"] == exp_id]
    if not rows:
        raise SystemExit("no runs registered for " + exp_id)
    registry_upsert([{"run_id": r["run_id"], "validity": "superseded" if r["validity"] in ("valid", "unverified") else r["validity"],
                      "note": (r["note"] + "; " if r["note"] else "") + "superseded %s: %s" % (now()[:10], reason)} for r in rows])
    print("marked %d runs of %s" % (len(rows), exp_id))

def cmd_prune(exp_id, yes):
    if not yes:
        raise SystemExit("prune deletes engine output; re-run with --yes")
    rows = [r for r in registry_rows() if r["experiment"] == exp_id and r["validity"] in ("superseded", "invalid")]
    updates = []
    for r in rows:
        rdir = run_dir_of(exp_id, r["arm"], int(r["seed"]))
        if not os.path.exists(rdir):
            continue
        for sub in os.listdir(rdir):
            if sub in ("config", "manifest.json"):
                continue
            p = os.path.join(rdir, sub)
            shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)
        updates.append({"run_id": r["run_id"], "status": "pruned",
                        "note": (r["note"] + "; " if r["note"] else "") + "pruned %s" % now()[:10]})
    if updates:
        registry_upsert(updates)
    print("pruned %d runs of %s (manifest and config kept)" % (len(updates), exp_id))

def cmd_status(exp_id=None):
    exps = {}
    for r in registry_rows():
        if exp_id and r["experiment"] != exp_id:
            continue
        e = exps.setdefault((r["kind"], r["experiment"]), {})
        key = "%s/%s" % (r["status"], r["validity"])
        e[key] = e.get(key, 0) + 1
    if not exps:
        print("(no runs registered)")
    for (kind, e), counts in sorted(exps.items()):
        notes = os.path.join(EXP_ROOT, kind, e, "NOTES.md")
        state = "no-notes" if not os.path.exists(notes) else ("analysis-pending" if NOTES_PLACEHOLDER in read_text(notes) else "analysed")
        print("%-6s %-42s %-16s %s" % (kind, e, state, "  ".join("%s=%d" % kv for kv in sorted(counts.items()))))


# ================================================================ main
def _opt(args, name, default=None, flag=False):
    if name in args:
        i = args.index(name)
        if flag:
            args.pop(i); return True
        val = args[i + 1]; del args[i:i + 2]; return val
    return default

if __name__ == "__main__":
    argv = sys.argv[1:]
    if not argv:
        print(__doc__); sys.exit(1)
    c, a = argv[0], argv[1:]
    if c == "new": cmd_new(a[0], a[1], " ".join(a[2:]))
    elif c == "plan": cmd_plan(a[0])
    elif c == "run":
        resume = _opt(a, "--resume", flag=True)
        workers = int(_opt(a, "--workers", 6)); max_sims = int(_opt(a, "--max-sims", 6))
        cmd_run(a[0], workers, max_sims, resume)
    elif c == "wait": poll = int(_opt(a, "--poll", 60)); cmd_wait(a[0], poll)
    elif c == "start": cmd_start(a[0], a[1])
    elif c == "finalize": cmd_finalize(a[0], a[1])
    elif c == "verify": cmd_verify(a[0])
    elif c == "table": cmd_table(a[0])
    elif c == "status": cmd_status(*(a[:1]))
    elif c == "canon-bump": cmd_canon_bump(" ".join(a))
    elif c == "stale-check": cmd_stale_check()
    elif c == "import-retro": cmd_import_retro(a[0])
    elif c == "supersede": cmd_supersede(a[0], " ".join(a[1:]))
    elif c == "prune": cmd_prune(a[0], _opt(a, "--yes", flag=True))
    else:
        print(__doc__); sys.exit(1)
