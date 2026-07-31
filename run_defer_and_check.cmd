@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo CLOSE-DRIFTCHECK5 seed0 START >> run_defer_and_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_close_driftcheck5_s0" 0 >> run_defer_and_check_runs.log 2>&1
if errorlevel 1 (echo CLOSE-DRIFTCHECK5 seed0 FAIL >> run_defer_and_check.log) else (echo CLOSE-DRIFTCHECK5 seed0 SUCCESS >> run_defer_and_check.log)
echo DEFER-2H seed0 START >> run_defer_and_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_defer.xconf" "output_defer_2h_s0" 0 >> run_defer_and_check_runs.log 2>&1
if errorlevel 1 (echo DEFER-2H seed0 FAIL >> run_defer_and_check.log) else (echo DEFER-2H seed0 SUCCESS >> run_defer_and_check.log)
echo DEFER-CHECK-DONE >> run_defer_and_check.log
