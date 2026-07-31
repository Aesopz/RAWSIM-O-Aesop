Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_gsweep.log -Force -ErrorAction SilentlyContinue
foreach($g in 4,3){ foreach($s in 0,1){ & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_epr_g$g.xconf" "output_g${g}_s$s" $s *>> run_gsweep_runs.log; "g$g-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_gsweep.log } }
"ALLDONE"|Out-File -Append -Encoding utf8 run_gsweep.log
