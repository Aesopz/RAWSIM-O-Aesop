Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_starv_robust.log -Force -ErrorAction SilentlyContinue
foreach($s in 2,3,4){
  & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g.xconf" "output_mincore_o100_s$s" $s *>> run_starv_robust_runs.log; "m3g-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_starv_robust.log
  & $cli "$sm\small.xlayo" $st "$sm\hgs_m3.xconf" "output_hgsm3_o100_s$s" $s *>> run_starv_robust_runs.log; "hgs-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_starv_robust.log
}
"ALLDONE"|Out-File -Append -Encoding utf8 run_starv_robust.log
