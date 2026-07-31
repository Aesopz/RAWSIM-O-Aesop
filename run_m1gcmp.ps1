Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_m1gcmp.log -Force -ErrorAction SilentlyContinue
foreach($s in 0,1){ & $cli "$sm\small.xlayo" $st "$sm\m1g.xconf" "output_m1g_o100i70_s$s" $s *>> run_m1gcmp_runs.log; "M1G-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_m1gcmp.log }
"ALLDONE"|Out-File -Append -Encoding utf8 run_m1gcmp.log
