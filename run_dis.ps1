Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
$o100="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_dis.log -Force -ErrorAction SilentlyContinue
foreach($x in "disA_nomulti","disB_nofloor","disC_nogate"){
  & $cli "$sm\small.xlayo" $o100 "$sm\m3g_$x.xconf" "output_$x`_o100_s0" 0 *>> run_dis_runs.log
  "$x $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_dis.log
}
"ALLDONE"|Out-File -Append -Encoding utf8 run_dis.log
