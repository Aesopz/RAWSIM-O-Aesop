Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
Remove-Item run_sweep.log -Force -ErrorAction SilentlyContinue
foreach($p in '05','2','5'){ foreach($g in '3','6','12'){
  & $cli "$sm\small.xlayo" "$sm\fixed_1150.xsett" "$sm\setlevel_p${p}_g${g}.xconf" "output_sw_p${p}_g${g}" 0 *>> run_sweep_runs.log
  "p$p g$g DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_sweep.log
}}
"ALLDONE" | Out-File -Append -Encoding utf8 run_sweep.log
