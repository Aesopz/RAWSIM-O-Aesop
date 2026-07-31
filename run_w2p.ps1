Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
Remove-Item run_w2p.log -Force -ErrorAction SilentlyContinue
function Run($sett,$cfg,$out,$seed,$tag){ & $cli "$sm\small.xlayo" "$sm\$sett" "$sm\$cfg" $out $seed *>> run_w2p_runs.log; "$tag DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_w2p.log }
Run "fixed_1150.xsett" "split_milp_m2eic_tier.xconf"       "output_w2p_tier_fx"    0 "TIER-FX"
Run "fixed_1150.xsett" "split_milp_m2eic_tier_now2p.xconf" "output_w2p_no_fx"      0 "NO-FX"
Run "small_o100_mu100_4h_inv50.xsett" "split_milp_m2eic_tier.xconf"       "output_w2p_tier_o100_s0" 0 "TIER-O100-S0"
Run "small_o100_mu100_4h_inv50.xsett" "split_milp_m2eic_tier.xconf"       "output_w2p_tier_o100_s1" 1 "TIER-O100-S1"
Run "small_o100_mu100_4h_inv50.xsett" "split_milp_m2eic_tier_now2p.xconf" "output_w2p_no_o100_s0"   0 "NO-O100-S0"
Run "small_o100_mu100_4h_inv50.xsett" "split_milp_m2eic_tier_now2p.xconf" "output_w2p_no_o100_s1"   1 "NO-O100-S1"
"ALLDONE" | Out-File -Append -Encoding utf8 run_w2p.log
