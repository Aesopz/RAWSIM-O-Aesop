# Generate a Fixed-mode order file that reproduces the Fill scenario's operating point.
#
# Why this exists: the pre-existing orders1150.xorders was built from Mu-500 (500 SKUs) and
# released orders slower than the system consumes them, so its pending pool averaged 43 vs
# Fill's 114.6. Both effects suppress consolidation - measured pile-on collapsed 4.58 -> 1.72,
# which makes it unusable for comparison against any Fill result.
#
# This generator matches the Fill scenario on both counts:
#   * Mu-100.xgenc  - same 100-SKU catalogue as small_o100_mu100_4h_inv70.xsett
#   * burst + drip  - BURST orders at t=0 (mirrors Fill's initial pool of 100), the rest spread
#                     evenly at a rate slightly above throughput so the pool stays ~115-150 deep
#
# Co-occurrence weights are deliberately not modelled: the engine has ProbToUseCoWeight = 0,
# so independent weighted sampling is the faithful behaviour, not a simplification.
#
# Usage: python gen_orders_fill.py [N_ORDERS] [HORIZON] [OUT] [XGENC] [BURST]

import re, sys, random

N_ORDERS = int(sys.argv[1]) if len(sys.argv) > 1 else 1350
HORIZON  = float(sys.argv[2]) if len(sys.argv) > 2 else 14400.0
OUT      = sys.argv[3] if len(sys.argv) > 3 else "Material/Instances/CoreBenchmark/small/orders_fill1350.xorders"
XGENC    = sys.argv[4] if len(sys.argv) > 4 else r"Material/Resources/Mu-100.xgenc"
BURST    = int(sys.argv[5]) if len(sys.argv) > 5 else 100
SEED     = 20260730

txt = open(XGENC, encoding="utf-8").read()

def parse_section(name):
    m = re.search(rf"<{name}>(.*?)</{name}>", txt, re.S)
    d = {}
    for k, v in re.findall(r"<Key>(\d+)</Key>\s*<Value>([^<]+)</Value>", m.group(1)):
        d[int(k)] = float(v)
    return d

prob = parse_section("ItemWeights")            # SKU selection probability weight
mass = parse_section("ItemDescriptionWeights") # physical weight per unit
skus = sorted(prob.keys())
tot  = sum(prob[s] for s in skus)
cum = []
acc = 0.0
for s in skus:
    acc += prob[s] / tot
    cum.append((acc, s))

rng = random.Random(SEED)

def normint(mean, sd, lo, hi):
    # Faithful mirror of RandomizerSimple.NextNormalInt(mean, std, min, max):
    #   NextNormalDouble(mean, std, min - 0.5, max + 0.5 - eps) then Math.Round.
    # The bounded draw REJECTS AND RESAMPLES (RandomizerSimple.cs:130-140); it does not clamp.
    # Clamping is what the older gen_orders.py did, and it biases every count toward the
    # lower bound - it is why orders1150.xorders averages 2.018 items/order where the engine
    # produces 2.365 under identical configured parameters.
    lower = lo - 0.5
    upper = hi + 0.5
    while True:
        x = rng.gauss(mean, sd)
        if lower <= x < upper:
            return int(round(x))

def pick_sku(exclude):
    for _ in range(20):
        r = rng.random()
        for c, s in cum:
            if r <= c:
                if s not in exclude:
                    return s
                break
    for s in skus:
        if s not in exclude:
            return s
    return skus[0]

def arrival_time(i):
    # First BURST orders are available immediately, mirroring Fill's initial pool; the rest
    # drip in evenly across the horizon so the pending pool never drains.
    if i < BURST:
        return 0.0
    rest = max(1, N_ORDERS - BURST)
    return (i - BURST + 1) * (HORIZON / rest)

lines = []
lines.append('<?xml version="1.0" encoding="utf-8"?>')
lines.append('<OrderList Type="SimpleItem">')
lines.append('  <ItemDescriptions>')
for s in skus:
    lines.append(f'    <ItemDescription ID="{s}" Type="SimpleItem" Weight="{mass[s]:.6f}" />')
lines.append('  </ItemDescriptions>')

demand = {s: 0 for s in skus}
order_xml = []
total_items = 0
for i in range(N_ORDERS):
    ts = arrival_time(i)
    nlines = normint(1, 1, 1, 5)
    used = set()
    pos = []
    for _ in range(nlines):
        s = pick_sku(used)
        used.add(s)
        cnt = normint(1, 1, 1, 5)
        pos.append((s, cnt))
        demand[s] += cnt
        total_items += cnt
    order_xml.append(f'    <Order TimeStamp="{ts:.4f}">')
    order_xml.append('      <Positions>')
    for s, cnt in pos:
        order_xml.append(f'        <Position ItemDescriptionID="{s}" Count="{cnt}" />')
    order_xml.append('      </Positions>')
    order_xml.append('    </Order>')

# Fixed replenishment stream, demand-weighted, phased over the arrival window.
MARGIN = 1.25
BUNDLE_SIZE = 8
supply_pool = []
for s in skus:
    units = int(round(demand[s] * MARGIN))
    while units > 0:
        sz = min(BUNDLE_SIZE, units)
        supply_pool.append((s, sz))
        units -= sz
rng.shuffle(supply_pool)
bstep = (HORIZON * 0.9) / max(1, len(supply_pool))
lines.append('  <ItemBundles>')
for j, (s, sz) in enumerate(supply_pool):
    lines.append(f'    <ItemBundle TimeStamp="{j * bstep:.4f}" ItemDescription="{s}" Size="{sz}" />')
lines.append('  </ItemBundles>')
lines.append('  <Orders>')
lines.extend(order_xml)
lines.append('  </Orders>')
lines.append('</OrderList>')

open(OUT, "w", encoding="utf-8").write("\n".join(lines))
print(f"wrote {OUT}")
print(f"  orders={N_ORDERS} (burst {BURST} at t=0, rest over [0,{HORIZON}])")
print(f"  SKUs={len(skus)} from {XGENC}")
print(f"  items={total_items}  items/order={total_items/N_ORDERS:.3f}")
print(f"  bundles={len(supply_pool)} (~{sum(sz for _, sz in supply_pool)} units, margin {MARGIN})")
