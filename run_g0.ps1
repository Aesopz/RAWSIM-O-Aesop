Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_g0.log -Force -ErrorAction SilentlyContinue
foreach($s in 0,1){ & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_epr_g0.xconf" "output_g0_s$s" $s *>> run_g0_runs.log; "g0-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_g0.log }
"ALLDONE"|Out-File -Append -Encoding utf8 run_g0.log
