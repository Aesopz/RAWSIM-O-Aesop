@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo M1G-FAIR START > run_m1g_fair.log
"%CLI%" "%SM%\small.xlayo" "%SM%\fixed_fill1350_inv70.xsett" "%SM%\m1g.xconf" "output_fair_m1g" 0 >> run_m1g_fair_runs.log 2>&1
if errorlevel 1 (echo m1g FAIL >> run_m1g_fair.log) else (echo m1g SUCCESS >> run_m1g_fair.log)
echo M1G-FAIR-DONE >> run_m1g_fair.log
