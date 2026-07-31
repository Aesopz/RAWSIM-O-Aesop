Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli = "RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"
$sm  = "Material\Instances\CoreBenchmark\small"
Remove-Item run_scd.log -Force -ErrorAction SilentlyContinue
function Run($sett,$out,$tag){
  & $cli "$sm\small.xlayo" "$sm\$sett" "$sm\split_milp_m2eic_tier_scd.xconf" $out 0 *>> run_scd_runs.log
  "$tag DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_scd.log
}
Run "small_o100_mu100_4h_inv50.xsett" "output_scd_o100" "O100"
Run "small_o200_mu100_4h_inv50.xsett" "output_scd_o200" "O200"
Run "small_o300_mu100_4h_inv50.xsett" "output_scd_o300" "O300"
"ALLDONE" | Out-File -Append -Encoding utf8 run_scd.log
