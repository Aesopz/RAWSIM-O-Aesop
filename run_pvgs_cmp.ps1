Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
$o100="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_pvgs_cmp.log -Force -ErrorAction SilentlyContinue
foreach($s in 0,1){
  & $cli "$sm\small.xlayo" $o100 "$sm\pvgs_e_eps_tier.xconf" "output_pvgs_o100_s$s" $s *>> run_pvgs_cmp_runs.log
  "pvgs-o100-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_pvgs_cmp.log
}
"ALLDONE"|Out-File -Append -Encoding utf8 run_pvgs_cmp.log
