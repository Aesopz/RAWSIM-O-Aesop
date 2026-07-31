Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
$o100="$sm\small_o100_mu100_4h_inv70.xsett"; $o200="$sm\small_o200_mu100_4h_inv70.xsett"
Remove-Item run_abl.log -Force -ErrorAction SilentlyContinue
foreach($a in 1,2,3,4){
  $cfg="$sm\m3g_abl$a.xconf"
  & $cli "$sm\small.xlayo" $o100 $cfg "output_abl${a}_o100_s0" 0 *>> run_abl_runs.log; "abl$a-o100 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_abl.log
  & $cli "$sm\small.xlayo" $o200 $cfg "output_abl${a}_o200_s0" 0 *>> run_abl_runs.log; "abl$a-o200 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_abl.log
}
"ALLDONE"|Out-File -Append -Encoding utf8 run_abl.log
