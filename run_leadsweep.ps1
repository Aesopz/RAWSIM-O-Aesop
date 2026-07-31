Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_leadsweep.log -Force -ErrorAction SilentlyContinue
foreach($L in 120,180,240,300){
  foreach($s in 0,1){
    & $cli "$sm\small.xlayo" $st "$sm\pvgs_db_L$L.xconf" "output_pvgsdb_L${L}_s$s" $s *>> run_leadsweep_runs.log
    "L$L-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_leadsweep.log
  }
}
"ALLDONE"|Out-File -Append -Encoding utf8 run_leadsweep.log
