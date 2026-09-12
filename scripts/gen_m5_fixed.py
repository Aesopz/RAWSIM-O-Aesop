# -*- coding: utf-8 -*-
"""Generate fixed-lambda M5 arms from the canonical hgs_m5.xconf.

Three edits only, mirroring the M4G escape-hatch arms (m4g_fxe*_u.xconf):
  LambdaFixed        the constant exchange rate under test
  MuFixed            lambda * (lines per order); pinning lambda alone leaves mu drifting
  LambdaIterations   1 = no Newton step, but the upper-bound jump can still fire once

Element order matters: XmlSerializer requires declaration order, so MuFixed goes
directly after LambdaFixed and LambdaIterations after IncrementalValuationEnabled
(GreedyM5Configuration, MethodConfigurationsOB.cs:1669+).
"""
import io, os, sys

SRC = r"Material\Instances\CoreBenchmark\small\m5_L_base.xconf"
DST = r"Material\Instances\CoreBenchmark\small\m5_L_fx%s.xconf"

def gen(lam, lpo):
    raw = io.open(SRC, encoding="utf-8", newline="").read()
    mu = lam * lpo
    out = []
    for line in raw.split("\n"):
        bare = line.strip()
        if bare.startswith("<LambdaFixed>"):
            out.append(line.replace("<LambdaFixed>0</LambdaFixed>",
                                    "<LambdaFixed>%g</LambdaFixed>" % lam))
            out.append(line.replace("<LambdaFixed>0</LambdaFixed>",
                                    "<MuFixed>%.4f</MuFixed>" % mu))
        elif bare.startswith("<IncrementalValuationEnabled>"):
            out.append(line)
            out.append(line.split("<Incremental")[0] +
                       "<LambdaIterations>1</LambdaIterations>" +
                       ("\r" if line.endswith("\r") else ""))
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
