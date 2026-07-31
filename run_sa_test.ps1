Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_sa_test.log -Force -ErrorAction SilentlyContinue
# SA-M3G (floor on) 2 seeds
foreach($s in 0,1){ & $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_sa.xconf" "output_sam3g_o100_s$s" $s *>> run_sa_test_runs.log; "sa-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_sa_test.log }
# zero-drift: SA config with flag OFF vs plain m3g (seed 0)
(Get-Content "$sm\split_milp_m3g_sa.xconf") -replace '<StarvationAwareDispatch>true</StarvationAwareDispatch>','<StarvationAwareDispatch>false</StarvationAwareDispatch>' | Set-Content "$sm\split_milp_m3g_sa_off.xconf"
& $cli "$sm\small.xlayo" $st "$sm\split_milp_m3g_sa_off.xconf" "output_sa_off_s0" 0 *>> run_sa_test_runs.log; "saoff-s0 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_sa_test.log
"ALLDONE"|Out-File -Append -Encoding utf8 run_sa_test.log
