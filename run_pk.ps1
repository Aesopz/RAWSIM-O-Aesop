Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli = "RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"
$sm  = "Material\Instances\CoreBenchmark\small"
Remove-Item output_pk_tier,output_pk_pk78,run_pk.log -Recurse -Force -ErrorAction SilentlyContinue
function Run($cfg,$out,$tag){
  & $cli "$sm\small.xlayo" "$sm\fixed_1150.xsett" "$sm\$cfg" $out 0 *>> run_pk_runs.log
  "$tag DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_pk.log
}
Run "split_milp_m2eic_tier.xconf"      "output_pk_tier" "TIER"
Run "split_milp_m2eic_tier_pk78.xconf" "output_pk_pk78" "PK78"
"ALLDONE" | Out-File -Append -Encoding utf8 run_pk.log
