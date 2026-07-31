@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo CLOSE-DRIFTCHECK4 seed0 START >> run_valuepp3_and_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_close_driftcheck4_s0" 0 >> run_valuepp3_and_check_runs.log 2>&1
if errorlevel 1 (echo CLOSE-DRIFTCHECK4 seed0 FAIL >> run_valuepp3_and_check.log) else (echo CLOSE-DRIFTCHECK4 seed0 SUCCESS >> run_valuepp3_and_check.log)
echo VALUEPP3-2H seed0 START >> run_valuepp3_and_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_valuepp.xconf" "output_valuepp3_2h_s0" 0 >> run_valuepp3_and_check_runs.log 2>&1
if errorlevel 1 (echo VALUEPP3-2H seed0 FAIL >> run_valuepp3_and_check.log) else (echo VALUEPP3-2H seed0 SUCCESS >> run_valuepp3_and_check.log)
echo VALUEPP3-CHECK-DONE >> run_valuepp3_and_check.log
