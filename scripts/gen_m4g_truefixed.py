# -*- coding: utf-8 -*-
"""Generate TRUE fixed-lambda M4G arms from the canonical m4g.xconf.

Three edits, all inside the pricing block:
  LambdaFixed          the constant exchange rate under test
  MuFixed              lambda * (lines per order); pinning lambda alone leaves mu drifting
  DinkelbachTolerance  1e9 - the tolerance break fires before the Newton step, so lambda
                       never moves. The upper-bound jump is checked EARLIER in the loop and
                       still fires, so a low lambda escapes instead of deadlocking.

Why not DinkelbachIterations=1: that still runs one full Newton step and ADOPTS it, so the
arm is fixed only at its starting point (measured 2026-09-11: declared 10..48 all executed
at a median lambda of 4.6-5.1). See M4GManager.cs ~2828 for the loop order.
"""
import io, os, sys

SRC = r"Material\Instances\CoreBenchmark\small\m4g.xconf"
DST = r"Material\Instances\CoreBenchmark\small\m4g_tf%s.xconf"

def gen(lam, lpo):
    raw = io.open(SRC, encoding="utf-8", newline="").read()
    out = []
    for line in raw.split("\n"):
        b = line.strip()
        if b.startswith("<LambdaFixed>"):
            out.append(line.replace("<LambdaFixed>0</LambdaFixed>",
                                    "<LambdaFixed>%g</LambdaFixed>" % lam))
            out.append(line.replace("<LambdaFixed>0</LambdaFixed>",
                                    "<MuFixed>%.4f</MuFixed>" % (lam * lpo)))
        elif b.startswith("<DinkelbachTolerance>"):
            out.append(line.replace(">0.5<", ">1000000000<"))
        else:
            out.append(line)
    tag = ("%g" % lam).replace(".", "p")
    path = DST % tag
    io.open(path, "w", encoding="utf-8", newline="").write("\n".join(out))
    return path

if __name__ == "__main__":
    lpo = float(sys.argv[1])
    for lam in [float(x) for x in sys.argv[2:]]:
        print(gen(lam, lpo))
