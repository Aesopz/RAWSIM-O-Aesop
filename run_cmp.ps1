Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
Remove-Item run_cmp.log -Force -ErrorAction SilentlyContinue
function Run($sett,$cfg,$out,$tag){ & $cli "$sm\small.xlayo" "$sm\$sett" "$sm\$cfg" $out 0 *>> run_cmp_runs.log; "$tag DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_cmp.log }
Run "fixed_1150.xsett" "m1g.xconf" "output_cmp_m1g_fx" "M1G-FX"
Run "fixed_1150.xsett" "split_milp_m2eic_tier_scd_val.xconf" "output_cmp_val_fx" "VAL-FX"
Run "fixed_1150.xsett" "split_milp_m2eic_tier_w40.xconf" "output_cmp_tw40_fx" "W40-FX"
Run "small_o100_mu100_4h_inv50.xsett" "m1g.xconf" "output_cmp_m1g_o100" "M1G-O100"
"ALLDONE" | Out-File -Append -Encoding utf8 run_cmp.log
