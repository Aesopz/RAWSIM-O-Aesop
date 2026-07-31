Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_lex.log -Force -ErrorAction SilentlyContinue
foreach($s in 0,1){ & $cli "$sm\small.xlayo" $st "$sm\hgs_m3_lex.xconf" "output_lex_s$s" $s *>> run_lex_runs.log; "lex-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_lex.log }
"ALLDONE"|Out-File -Append -Encoding utf8 run_lex.log
