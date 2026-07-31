Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
$o100="$sm\small_o100_mu100_4h_inv70.xsett"; $o200="$sm\small_o200_mu100_4h_inv70.xsett"
Remove-Item run_mincore_s1.log -Force -ErrorAction SilentlyContinue
& $cli "$sm\small.xlayo" $o100 "$sm\m3g_mincore.xconf" "output_mincore_o100_s1" 1 *>> run_mincore_s1_runs.log; "mincore-o100-s1 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_mincore_s1.log
& $cli "$sm\small.xlayo" $o200 "$sm\m3g_mincore.xconf" "output_mincore_o200_s1" 1 *>> run_mincore_s1_runs.log; "mincore-o200-s1 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_mincore_s1.log
"ALLDONE"|Out-File -Append -Encoding utf8 run_mincore_s1.log
