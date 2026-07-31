Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
$o100="$sm\small_o100_mu100_4h_inv70.xsett"; $o200="$sm\small_o200_mu100_4h_inv70.xsett"
Remove-Item run_mincore.log -Force -ErrorAction SilentlyContinue
foreach($x in "mincore","mincore_noab"){
  & $cli "$sm\small.xlayo" $o100 "$sm\m3g_$x.xconf" "output_$x`_o100_s0" 0 *>> run_mincore_runs.log; "$x-o100 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_mincore.log
}
# deadlock check only for the leaner core
& $cli "$sm\small.xlayo" $o200 "$sm\m3g_mincore.xconf" "output_mincore_o200_s0" 0 *>> run_mincore_runs.log; "mincore-o200 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_mincore.log
"ALLDONE"|Out-File -Append -Encoding utf8 run_mincore.log
