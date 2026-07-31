Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_tsweep.log -Force -ErrorAction SilentlyContinue
foreach($t in 2,3){ foreach($s in 0,1){ & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_T$t.xconf" "output_T${t}_s$s" $s *>> run_tsweep_runs.log; "T$t-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_tsweep.log } }
"ALLDONE"|Out-File -Append -Encoding utf8 run_tsweep.log
