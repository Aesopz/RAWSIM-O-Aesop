Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv50.xsett"
Remove-Item run_mu100.log -Force -ErrorAction SilentlyContinue
& $cli "$sm\small_10bot.xlayo" $st "$sm\setlevel_p5_g6.xconf" output_mu100_sl_10 0 *>> run_mu100_runs.log; "SL10 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_mu100.log
& $cli "$sm\small_10bot.xlayo" $st "$sm\split_milp_m2eic_tier.xconf" output_mu100_tier_10 0 *>> run_mu100_runs.log; "TIER10 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_mu100.log
& $cli "$sm\small_5bot.xlayo" $st "$sm\setlevel_p5_g6.xconf" output_mu100_sl_5 0 *>> run_mu100_runs.log; "SL5 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_mu100.log
& $cli "$sm\small_5bot.xlayo" $st "$sm\split_milp_m2eic_tier.xconf" output_mu100_tier_5 0 *>> run_mu100_runs.log; "TIER5 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_mu100.log
& $cli "$sm\small_5bot.xlayo" $st "$sm\m1g.xconf" output_mu100_m1g_5 0 *>> run_mu100_runs.log; "M1G5 $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_mu100.log
"ALLDONE"|Out-File -Append -Encoding utf8 run_mu100.log
