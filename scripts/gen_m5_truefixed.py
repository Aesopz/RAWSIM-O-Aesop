# -*- coding: utf-8 -*-
"""Generate TRUE fixed-lambda M5 arms from the large-scale base config.

Three edits:
  LambdaFixed      the constant exchange rate under test
  MuFixed          lambda * (lines per order)
  LambdaTolerance  1e9 - the tolerance break fires before the Newton step, so lambda never
                   moves. The upper-bound jump sits EARLIER in the loop and still fires, so
                   a low lambda escapes instead of deadlocking.

LambdaIterations is left at the canonical default: the break is what stops Newton, so the
iteration budget stays identical to the dynamic arm and lambda mobility is the only
difference between the arms. Element order follows GreedyM5Configuration's declaration
order - LambdaTolerance goes after IncrementalValuationEnabled, before LambdaEscalations.
"""
import io, sys

SRC = r"Material\Instances\CoreBenchmark\small\m5_L_base.xconf"
DST = r"Material\Instances\CoreBenchmark\small\m5_L_tf%s.xconf"

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
        elif b.startswith("<IncrementalValuationEnabled>"):
            out.append(line)
            ind = line[:len(line) - len(line.lstrip())]
            cr = "\r" if line.endswith("\r") else ""
            out.append(ind + "<LambdaTolerance>1000000000</LambdaTolerance>" + cr)
        else:
            out.append(line)
    tag = ("%g" % lam).replace(".", "p")
    io.open(DST % tag, "w", encoding="utf-8", newline="").write("\n".join(out))
    return DST % tag

if __name__ == "__main__":
    lpo = float(sys.argv[1])
    for lam in [float(x) for x in sys.argv[2:]]:
        print(gen(lam, lpo))
