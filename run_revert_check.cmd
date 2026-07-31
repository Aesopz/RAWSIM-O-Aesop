@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo REVERT-CLOSE seed0 START >> run_revert_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_revert_close_s0" 0 >> run_revert_check_runs.log 2>&1
if errorlevel 1 (echo REVERT-CLOSE seed0 FAIL >> run_revert_check.log) else (echo REVERT-CLOSE seed0 SUCCESS >> run_revert_check.log)
echo REVERT-CHECK-DONE >> run_revert_check.log
