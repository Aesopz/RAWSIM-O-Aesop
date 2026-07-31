Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
Remove-Item run_5bot.log -Force -ErrorAction SilentlyContinue
& $cli "$sm\small_5bot.xlayo" "$sm\fixed_1150.xsett" "$sm\setlevel_p5_g6.xconf" output_5bot_setlevel 0 *>> run_5bot_runs.log
"SETLEVEL DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_5bot.log
& $cli "$sm\small_5bot.xlayo" "$sm\fixed_1150.xsett" "$sm\split_milp_m2eic_tier.xconf" output_5bot_tier 0 *>> run_5bot_runs.log
"TIER DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_5bot.log
& $cli "$sm\small_5bot.xlayo" "$sm\fixed_1150.xsett" "$sm\m1g.xconf" output_5bot_m1g 0 *>> run_5bot_runs.log
"M1G DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_5bot.log
"ALLDONE" | Out-File -Append -Encoding utf8 run_5bot.log
