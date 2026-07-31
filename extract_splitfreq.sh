#!/bin/bash
# Bucket split-children over simulation time for M3G and HGS-M3.
# Emits JS-ready arrays. Bucket = 300s (5 min) over 14400s (4h) = 48 buckets.
cd "C:/Users/Aesop/Desktop/EE-RAWSim-O_PP"
BUCKET=300; NB=48

emit() { # $1=label $2=csv $3=timeCol $4=childrenCol $5=unitsCol $6=dispatchCol
  awk -F, -v B=$BUCKET -v NB=$NB -v tc=$3 -v cc=$4 -v uc=$5 -v dc=$6 -v lab="$1" '
  NR==1 {next}
  {
    t=$tc+0; b=int(t/B); if(b>=NB) b=NB-1; if(b<0) b=0;
    ch[b]+=$cc+0; un[b]+=$uc+0; dp[b]+=$dc+0; dec[b]+=1;
    if($cc+0>0) sd[b]+=1;
    totCh+=$cc+0; totDec+=1; if($cc+0>0) totSd+=1; totUn+=$uc+0;
  }
  END{
    printf "%s: {children:[", lab;
    for(i=0;i<NB;i++) printf "%s%d", (i?",":""), ch[i];
    printf "], units:[";
    for(i=0;i<NB;i++) printf "%s%d", (i?",":""), un[i];
    printf "], decisions:[";
    for(i=0;i<NB;i++) printf "%s%d", (i?",":""), dec[i];
    printf "], splitDecisions:[";
    for(i=0;i<NB;i++) printf "%s%d", (i?",":""), sd[i];
    printf "], dispatch:[";
    for(i=0;i<NB;i++) printf "%s%d", (i?",":""), dp[i];
    printf "], totals:{children:%d, decisions:%d, splitDecisions:%d, units:%d}},\n", totCh, totDec, totSd, totUn;
  }' "$2"
}

M=$(ls output_sf_split_milp_m3g/*/splitm2eic_decision_log.csv 2>/dev/null | head -1)
H=$(ls output_sf_hgs_m3/*/pvgs_decision_log.csv 2>/dev/null | head -1)
echo "// M3G cols: decision,time(2),solved,pendingOrders,stationsWithCap,podsInModel,xps(7),children(8),fastPath,units(10)"
echo "// HGS cols: decision,time(2),pendingOrders,stationsWithCap,podsInModel,dispatched(6),children(7),fastPath,units(9)"
[ -n "$M" ] && emit "m3g" "$M" 2 8 10 7 || echo "// M3G log missing"
[ -n "$H" ] && emit "hgs" "$H" 2 7 9 6 || echo "// HGS log missing"
