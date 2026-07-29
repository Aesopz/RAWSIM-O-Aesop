# M2E-AE Pod-Centric Alignment: 7200-Tick Acceptance

## 1. Acceptance rule

The acceptance case is the complete `small` instance, `small_o100_mu100.xsett`,
seed `0`, and `7200 ticks`. Short smoke runs and one-decision results are used
only for diagnosis and are not acceptance evidence.

The comparison reference is the existing PVGS-E run under
`output_acc_pvgse_s0` with the same instance, seed, and horizon.

## 2. Reference

| Method | Completed orders | TP | PO | Output arrivals | Distance (m) | Order distance (m/order) | Energy (kJ/order) |
|---|---:|---:|---:|---:|---:|---:|---:|
| PVGS-E | 658 | 329 | 3.870588 | 170 | 13163.471 | 20.0053 | 1.62393 |
| M2E-AE target=2, lead=70s | 653 | 326.5 | 3.863905 | 169 | 14117.804 | 21.6199 | 1.76648 |

## 3. Tested full-horizon variants

| Variant | Completed orders | TP | PO | Output arrivals | Distance (m) | Result |
|---|---:|---:|---:|---:|---:|---|
| target=1, lead=70s | 630 | 315 | 3.888889 | 162 | Fewer trips and lower TP | Fails TP |
| target=2, lead=70s | 653 | 326.5 | 3.863905 | 169 | Best tested balance | Fails by 5 orders and 1 arrival |
| target=2, lead=50s | 633 | 316.5 | 3.723529 | 170 | More aggressive timing harms PO | Fails |
| target=2, lead=0s | 619 | 309.5 | 3.438889 | 180 | Over-supply and weaker OA | Fails |
| target=3, lead=70s | 641 | 320.5 | 3.391534 | 189 | Excess pipeline supply lowers PO | Fails |

## 4. Interpretation

The new adaptive path is no longer losing because it cannot use a processing
pod. With target=2 and lead=70s it reaches `PO=3.863905`, almost equal to
PVGS-E, while using fewer output arrivals. The remaining gap is primarily a
periodic-supply and OA timing gap: one fewer output arrival corresponds to five
fewer completed orders in this run.

Removing the lead gate causes more pod trips but substantially reduces PO. The
correct control is therefore not “always dispatch a new pod when a slot opens”.
It needs a two-pod station pipeline, a bounded lead gate, and an exact OA that
continues to prioritize processing-pod completion.

## 5. Current configuration

The checked-in small-case experiment is restored to the best full-horizon
candidate:

```xml
<AdaptiveFuturePodTarget>2</AdaptiveFuturePodTarget>
<AdaptivePeriodicSupplyLeadTimeSec>70</AdaptivePeriodicSupplyLeadTimeSec>
<AdaptiveMaxPodBurst>1</AdaptiveMaxPodBurst>
<AdaptiveRiskPrefetch>true</AdaptiveRiskPrefetch>
```

`AdaptiveExactResweeps` remains opt-in and the non-adaptive defaults remain
unchanged.

## 6. Verification status

The current implementation builds successfully. The focused adaptive math
tests pass (`77/77` in the last complete test run). The full 7200-tick M2E-AE
run exits successfully, but the acceptance comparison is **not passed**:

```text
M2E-AE: 653 orders, TP 326.5, PO 3.863905
PVGS-E: 658 orders, TP 329.0, PO 3.870588
```

The next algorithmic work should target the final OA-to-station timing and
pipeline arrival, rather than increasing the future-pod target or removing the
lead gate.
