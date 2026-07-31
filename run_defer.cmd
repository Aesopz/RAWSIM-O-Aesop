@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo DEFER START > run_defer.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv70.xsett" "%SM%\split_milp_m3g_defer.xconf" "output_defer_s0" 0 >> run_defer_runs.log 2>&1
if errorlevel 1 (echo defer-S0 FAIL >> run_defer.log) else (echo defer-S0 SUCCESS >> run_defer.log)
echo DEFER-DONE >> run_defer.log
