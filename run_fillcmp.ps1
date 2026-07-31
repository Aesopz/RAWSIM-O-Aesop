Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_fillcmp.log -Force -ErrorAction SilentlyContinue
foreach($s in 0,1){
  & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g.xconf" "output_fc_m3g_s$s" $s *>> run_fillcmp_runs.log; "M3G-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_fillcmp.log
  & $cli "$sm\small.xlayo" $st "$sm\split_milp_m2eic_tier.xconf" "output_fc_tier_s$s" $s *>> run_fillcmp_runs.log; "TIER-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_fillcmp.log
}
"ALLDONE"|Out-File -Append -Encoding utf8 run_fillcmp.log
