Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_l30wp.log -Force -ErrorAction SilentlyContinue
foreach($wp in 40,60){ foreach($s in 0,1){ & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_l30wp$wp.xconf" "output_l30wp${wp}_s$s" $s *>> run_l30wp_runs.log; "wp$wp-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_l30wp.log } }
"ALLDONE"|Out-File -Append -Encoding utf8 run_l30wp.log
