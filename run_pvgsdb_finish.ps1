Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
Remove-Item run_pvgsdb_finish.log -Force -ErrorAction SilentlyContinue
& $cli "$sm\small.xlayo" "$sm\small_o100_mu100_4h_inv70.xsett" "$sm\pvgs_db.xconf" "output_pvgsdb_o100_s1" 1 *>> run_pvgsdb_finish_runs.log; "o100-s1 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_pvgsdb_finish.log
& $cli "$sm\small.xlayo" "$sm\small_o200_mu100_4h_inv70.xsett" "$sm\pvgs_db.xconf" "output_pvgsdb_o200_s0" 0 *>> run_pvgsdb_finish_runs.log; "o200-s0 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_pvgsdb_finish.log
"ALLDONE"|Out-File -Append -Encoding utf8 run_pvgsdb_finish.log
