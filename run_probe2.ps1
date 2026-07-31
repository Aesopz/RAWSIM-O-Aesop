Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_probe.xsett"
Remove-Item run_probe2.log -Force -ErrorAction SilentlyContinue
& $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_line30.xconf" "output_probe_line30_s0" 0 *>> run_probe2_runs.log; "line30 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_probe2.log
"ALLDONE"|Out-File -Append -Encoding utf8 run_probe2.log
