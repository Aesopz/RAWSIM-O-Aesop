@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo CLOSE-DRIFTCHECK seed0 START >> run_value_and_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_close_driftcheck_s0" 0 >> run_value_and_check_runs.log 2>&1
if errorlevel 1 (echo CLOSE-DRIFTCHECK seed0 FAIL >> run_value_and_check.log) else (echo CLOSE-DRIFTCHECK seed0 SUCCESS >> run_value_and_check.log)
echo VALUE-2H seed0 START >> run_value_and_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_value.xconf" "output_value_2h_s0" 0 >> run_value_and_check_runs.log 2>&1
if errorlevel 1 (echo VALUE-2H seed0 FAIL >> run_value_and_check.log) else (echo VALUE-2H seed0 SUCCESS >> run_value_and_check.log)
echo VALUE-CHECK-DONE >> run_value_and_check.log
