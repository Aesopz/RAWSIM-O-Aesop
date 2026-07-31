Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
$m3g="$sm\split_milp_m3g.xconf"; $m1g="$sm\m1g.xconf"
Remove-Item run_sweep_o.log -Force -ErrorAction SilentlyContinue
# o50/o75/o150: both models, both seeds
foreach($n in 50,75,150){
  $st="$sm\small_o${n}_mu100_4h_inv70.xsett"
  foreach($s in 0,1){
    & $cli "$sm\small.xlayo" $st $m3g "output_sw_m3g_o${n}_s$s" $s *>> run_sweep_o_runs.log; "m3g-o$n-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_sweep_o.log
    & $cli "$sm\small.xlayo" $st $m1g "output_sw_m1g_o${n}_s$s" $s *>> run_sweep_o_runs.log; "m1g-o$n-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_sweep_o.log
  }
}
# o200: M1G only (M3G already done)
foreach($s in 0,1){
  & $cli "$sm\small.xlayo" "$sm\small_o200_mu100_4h_inv70.xsett" $m1g "output_sw_m1g_o200_s$s" $s *>> run_sweep_o_runs.log; "m1g-o200-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_sweep_o.log
}
"ALLDONE"|Out-File -Append -Encoding utf8 run_sweep_o.log
