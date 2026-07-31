Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
Remove-Item run_t3.log -Force -ErrorAction SilentlyContinue
& $cli "$sm\small.xlayo" "$sm\fixed_1150.xsett" "$sm\split_milp_m2eic_tier.xconf" output_t3_drift 0 *>> run_t3_runs.log
"DRIFT DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_t3.log
& $cli "$sm\small.xlayo" "$sm\fixed_1150.xsett" "$sm\split_milp_m2eic_setlevel.xconf" output_t3_smoke 0 *>> run_t3_runs.log
"SMOKE DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_t3.log
"ALLDONE" | Out-File -Append -Encoding utf8 run_t3.log
