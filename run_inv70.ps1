Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
Remove-Item run_inv70.log -Force -ErrorAction SilentlyContinue
foreach($oc in '200','300'){
  & $cli "$sm\small.xlayo" "$sm\small_o${oc}_mu100_4h_inv70.xsett" "$sm\split_milp_m2eic_tier.xconf" "output_i70_tier_o$oc" 0 *>> run_inv70_runs.log
  "TIER-o$oc $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_inv70.log
  & $cli "$sm\small.xlayo" "$sm\small_o${oc}_mu100_4h_inv70.xsett" "$sm\setlevel_p5_g6.xconf" "output_i70_sl_o$oc" 0 *>> run_inv70_runs.log
  "SL-o$oc $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_inv70.log
}
"ALLDONE"|Out-File -Append -Encoding utf8 run_inv70.log
