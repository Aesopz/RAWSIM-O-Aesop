# -*- coding: utf-8 -*-
"""F10: flag figures whose provenance points at superseded runs or an older Canon version.

usage: python scripts/fig_stale_check.py [root ...]     (default: experiments docs)
Writes STALE.md next to each stale figure (and removes it when the figure is current again).
"""
import sys, os, io, json, glob, csv
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import exp as E

def main(roots):
    cv = E.current_canon_version_checked()["version"]
    validity = {r["run_id"]: r["validity"] for r in E.registry_rows()}
    stale, fresh = [], 0
    for root in roots:
        for prov_path in glob.glob(os.path.join(ROOT, root, "**", "*.provenance.json"), recursive=True):
            prov = json.load(io.open(prov_path, encoding="utf-8"))
            reasons = []
            if prov.get("canon_version") not in (None, cv):
                reasons.append("figure built under Canon v%s, current is v%s" % (prov.get("canon_version"), cv))
            for rid in prov.get("runs", []):
                v = validity.get(rid)
                if v is None: reasons.append("run %s not in registry" % rid)
                elif v != "valid": reasons.append("run %s is %s" % (rid, v))
            fig = prov_path[:-len(".provenance.json")]
            stale_md = os.path.join(os.path.dirname(fig), "STALE.md")
            if reasons:
                stale.append((fig, reasons))
                lines = ["# STALE FIGURE", "", "`%s` must not be delivered until regenerated." % os.path.basename(fig), ""] + ["- " + r for r in reasons[:12]]
                io.open(stale_md, "w", encoding="utf-8").write("\n".join(lines) + "\n")
            else:
                fresh += 1
                if os.path.exists(stale_md):
                    others = [p for p in glob.glob(os.path.join(os.path.dirname(fig), "*.provenance.json")) if p != prov_path]
                    if not others: os.remove(stale_md)
    for fig, reasons in stale:
        print("STALE  %s\n       %s" % (os.path.relpath(fig, ROOT), "; ".join(reasons[:3])))
    print("fig-stale-check (Canon v%s): %d current, %d stale" % (cv, fresh, len(stale)))
    return 1 if stale else 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:] or ["experiments", "docs"]))
