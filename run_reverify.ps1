Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
Remove-Item run_reverify.log -Force -ErrorAction SilentlyContinue
& $cli "$sm\small_10bot.xlayo" "$sm\small_o100_mu100_4h_inv50.xsett" "$sm\m1g.xconf" output_rv_m1g_mu100_10bot 0 *>> run_reverify_runs.log
"M1G-MU100-10 DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_reverify.log
& $cli "$sm\small.xlayo" "$sm\small_o100_mu100_4h_inv50.xsett" "$sm\m1g.xconf" output_rv_m1g_mu100_15bot 0 *>> run_reverify_runs.log
"M1G-MU100-15 DONE exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 run_reverify.log
"ALLDONE" | Out-File -Append -Encoding utf8 run_reverify.log
