Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli = "RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"
$sm  = "Material\Instances\CoreBenchmark\small"
Remove-Item run_bl.log -Force -ErrorAction SilentlyContinue
function Run($sett,$out,$seed,$tag){
  & $cli "$sm\small.xlayo" "$sm\$sett" "$sm\split_milp_m2eic_tier.xconf" $out $seed *>> run_bl_runs.log
  "$tag DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_bl.log
}
foreach($oc in 100,200,300){
  foreach($s in 0,1){
    Run "small_o${oc}_mu100_4h_inv50.xsett" "output_bl_o${oc}_s${s}" $s "O${oc}-S${s}"
  }
}
"ALLDONE" | Out-File -Append -Encoding utf8 run_bl.log
