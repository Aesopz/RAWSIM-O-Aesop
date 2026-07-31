Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_line.log -Force -ErrorAction SilentlyContinue
foreach($w in 5,20){ foreach($s in 0,1){ & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_line$w.xconf" "output_line${w}_s$s" $s *>> run_line_runs.log; "line$w-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_line.log } }
"ALLDONE"|Out-File -Append -Encoding utf8 run_line.log
