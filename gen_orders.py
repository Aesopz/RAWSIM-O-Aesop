import re, sys, random, xml.sax.saxutils as su

XGENC = r"Material/Resources/Mu-500.xgenc"
N_ORDERS = int(sys.argv[1]) if len(sys.argv) > 1 else 3000
HORIZON  = float(sys.argv[2]) if len(sys.argv) > 2 else 32400.0  # arrivals spread over [0,HORIZON]
OUT      = sys.argv[3] if len(sys.argv) > 3 else "Material/Instances/CoreBenchmark/small/orders3000.xorders"
SEED     = 20260724

txt = open(XGENC, encoding="utf-8").read()

def parse_section(name):
    m = re.search(rf"<{name}>(.*?)</{name}>", txt, re.S)
    d = {}
    for k, v in re.findall(r"<Key>(\d+)</Key>\s*<Value>([^<]+)</Value>", m.group(1)):
        d[int(k)] = float(v)
    return d

prob = parse_section("ItemWeights")            # SKU selection probability weight (doc: transformed into probabilities)
mass = parse_section("ItemDescriptionWeights") # physical weight per unit (Uniform[2,8])
skus = sorted(prob.keys())
tot  = sum(prob[s] for s in skus)
# cumulative distribution for weighted sampling
cum = []
acc = 0.0
for s in skus:
    acc += prob[s] / tot
    cum.append((acc, s))

rng = random.Random(SEED)

def normint(mean, sd, lo, hi):
    # mirror engine NextNormalInt: normal, round, clamp
    x = rng.gauss(mean, sd)
    v = int(round(x))
    return max(lo, min(hi, v))

def pick_sku(exclude):
    for _ in range(20):
        r = rng.random()
        # binary-ish linear scan (500 items, fine)
        for c, s in cum:
            if r <= c:
                if s not in exclude:
                    return s
                break
    # fallback: any not-excluded
    for s in skus:
        if s not in exclude:
            return s
    return skus[0]

lines = []
lines.append('<?xml version="1.0" encoding="utf-8"?>')
lines.append('<OrderList Type="SimpleItem">')
lines.append('  <ItemDescriptions>')
for s in skus:
    lines.append(f'    <ItemDescription ID="{s}" Type="SimpleItem" Weight="{mass[s]:.6f}" />')
lines.append('  </ItemDescriptions>')

# --- generate orders first (also accumulate per-SKU demand for bundle sizing) ---
demand = {s: 0 for s in skus}
order_xml = []
step = HORIZON / max(1, N_ORDERS - 1)
total_items = 0
for i in range(N_ORDERS):
    ts = i * step
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

# --- generate a fixed replenishment (bundle) stream, demand-weighted, size ~8, ---
# --- total ~= MARGIN x demand, phased over the arrival window (mirrors Fill's   ---
# --- weighted random bundle generation but as a deterministic stream).          ---
MARGIN = 1.25
BUNDLE_SIZE = 8
target_units = total_items * MARGIN
# per-SKU supply proportional to its demand (so scarce SKUs stay scarce, hot SKUs stocked)
dtot = sum(demand.values())
bundles = []  # (timestamp, sku, size)
supply_pool = []
for s in skus:
    units = int(round(demand[s] * MARGIN))
    while units > 0:
        sz = min(BUNDLE_SIZE, units)
        supply_pool.append((s, sz))
        units -= sz
# shuffle so SKUs interleave in time, then phase evenly across 0..0.9*HORIZON
rng.shuffle(supply_pool)
bstep = (HORIZON * 0.9) / max(1, len(supply_pool))
for j, (s, sz) in enumerate(supply_pool):
    bundles.append((j * bstep, s, sz))

lines.append('  <ItemBundles>')
for ts, s, sz in bundles:
    lines.append(f'    <ItemBundle TimeStamp="{ts:.4f}" ItemDescription="{s}" Size="{sz}" />')
lines.append('  </ItemBundles>')
lines.append('  <Orders>')
lines.extend(order_xml)
lines.append('  </Orders>')
lines.append('</OrderList>')
print(f"total demanded items={total_items}, bundles={len(supply_pool)} (~{sum(sz for _,sz in supply_pool)} units, margin {MARGIN})")

open(OUT, "w", encoding="utf-8").write("\n".join(lines))
# summary
import statistics
# recount for report
print(f"wrote {OUT}: {N_ORDERS} orders, {len(skus)} SKUs, horizon [0,{HORIZON}] step={step:.2f}")
