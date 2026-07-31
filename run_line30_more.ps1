Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_line30_more.log -Force -ErrorAction SilentlyContinue
foreach($s in 2,3,4){ & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_line30.xconf" "output_line30_s$s" $s *>> run_line30_more_runs.log; "line30-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_line30_more.log }
"ALLDONE"|Out-File -Append -Encoding utf8 run_line30_more.log
