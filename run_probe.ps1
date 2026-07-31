Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_probe.xsett"
Remove-Item run_probe.log -Force -ErrorAction SilentlyContinue
& $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g.xconf" "output_probe_m3g_s0" 0 *>> run_probe_runs.log; "m3g $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_probe.log
& $cli "$sm\small.xlayo" $st "$sm\hgs_m3.xconf" "output_probe_hgs_s0" 0 *>> run_probe_runs.log; "hgs $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_probe.log
"ALLDONE"|Out-File -Append -Encoding utf8 run_probe.log
