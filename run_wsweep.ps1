Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_wsweep.log -Force -ErrorAction SilentlyContinue
foreach($w in 2,5,10,20){
  foreach($s in 0,1){
    & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_sa_w$w.xconf" "output_saw${w}_s$s" $s *>> run_wsweep_runs.log
    "w$w-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_wsweep.log
  }
}
"ALLDONE"|Out-File -Append -Encoding utf8 run_wsweep.log
