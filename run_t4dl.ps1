Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
Remove-Item run_t4dl.log -Force -ErrorAction SilentlyContinue
& $cli "$sm\small.xlayo" "$sm\small_o200_mu100_4h_inv50.xsett" "$sm\split_milp_m2eic_setlevel.xconf" output_t4_o200 0 *>> run_t4dl_runs.log
"O200 DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_t4dl.log
& $cli "$sm\small.xlayo" "$sm\small_o300_mu100_4h_inv50.xsett" "$sm\split_milp_m2eic_setlevel.xconf" output_t4_o300 0 *>> run_t4dl_runs.log
"O300 DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_t4dl.log
"ALLDONE" | Out-File -Append -Encoding utf8 run_t4dl.log
