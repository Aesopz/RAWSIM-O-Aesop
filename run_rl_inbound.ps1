Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli = "RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"
$sm  = "Material\Instances\CoreBenchmark\small"
Remove-Item qtable_inbound.txt,qtable_inbound.txt.statelog.csv -ErrorAction SilentlyContinue

function Run($sett, $cfg, $out, $seed, $tag) {
    "$tag START $(Get-Date -Format o)" | Add-Content run_rl_inbound.log
    & $cli "$sm\small.xlayo" "$sm\$sett" "$sm\$cfg" $out $seed *>> run_rl_inbound_runs.log
    "$tag DONE exit=$LASTEXITCODE" | Add-Content run_rl_inbound.log
}

Run "rl_train.xsett" "split_milp_m2eic_tier.xconf" "output_rltrain_s0"  0 "TRAIN-S0"
Run "rl_train.xsett" "split_milp_m2eic_tier.xconf" "output_rltrain_s1"  1 "TRAIN-S1"
Run "rl_eval.xsett"  "split_milp_m2eic_tier.xconf" "output_rleval_rl_s2" 2 "EVAL-RL-S2"
Run "small_o100_mu100_4h_inv50.xsett" "split_milp_m2eic_tier.xconf" "output_rleval_T1_s2" 2 "EVAL-T1-S2"
Run "small_o100_mu100_4h_inv50.xsett" "tier_T2.xconf" "output_rleval_T2_s2" 2 "EVAL-T2-S2"
Run "small_o100_mu100_4h_inv50.xsett" "tier_T3.xconf" "output_rleval_T3_s2" 2 "EVAL-T3-S2"
"ALL-DONE $(Get-Date -Format o)" | Add-Content run_rl_inbound.log
