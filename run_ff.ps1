Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_ff.log -Force -ErrorAction SilentlyContinue
foreach($s in 0,1){ & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_ff.xconf" "output_ff_s$s" $s *>> run_ff_runs.log; "ff-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_ff.log }
"ALLDONE"|Out-File -Append -Encoding utf8 run_ff.log
