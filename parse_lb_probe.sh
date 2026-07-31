#!/bin/bash
# Parse LB probe KPIs. Cols: TP, starvation(s), noOrderIdle(s), EOR(kJ), pileOn, items, orders, podVisits, PO(ord/visit), IPO(item/visit)
cd "C:/Users/Aesop/Desktop/EE-RAWSim-O_PP"
printf "%-16s %8s %10s %10s %7s %8s %7s %7s %8s %6s %6s\n" config TP starv noOrdIdle EOR pileOn items orders visits PO IPO
for cfg in m1e m1elb6 m1elb12; do
  for s in 0 1; do
    D=$(ls -d output_lb_${cfg}_s${s}/*/ 2>/dev/null | head -1)
    if [ -z "$D" ]; then printf "%-16s  (no output)\n" "${cfg}_s${s}"; continue; fi
    f="$D/statistics.txt"
    g(){ grep -E "^$1:" "$f" | tail -1 | awk '{print $2}'; }
    TP=$(g StatThroughputOrdersPerHour); ST=$(g StatStationStarvationTimeSec); NI=$(g StatStationNoOrderIdleTimeSec)
    EOR=$(g StatEnergyPerOrderKJ); PON=$(g StatSystemOrderPileOn); IT=$(g StatOverallItemsHandled)
    OR=$(g StatOverallOrdersHandled); PV=$(g StatPodVisitCount)
    PO=$(awk "BEGIN{if($PV>0)printf \"%.2f\",$OR/$PV}"); IPO=$(awk "BEGIN{if($PV>0)printf \"%.2f\",$IT/$PV}")
    printf "%-16s %8.1f %10.1f %10.1f %7.3f %8.3f %7s %7s %8s %6s %6s\n" "${cfg}_s${s}" "$TP" "$ST" "$NI" "$EOR" "$PON" "$IT" "$OR" "$PV" "$PO" "$IPO"
  done
done
