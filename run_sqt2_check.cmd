@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo TIER-DRIFTCHECK2 seed0 START >> run_sqt2_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_tier_driftcheck2_s0" 0 >> run_sqt2_check_runs.log 2>&1
if errorlevel 1 (echo TIER-DRIFTCHECK2 seed0 FAIL >> run_sqt2_check.log) else (echo TIER-DRIFTCHECK2 seed0 SUCCESS >> run_sqt2_check.log)
echo SQT2-MU500 seed0 START >> run_sqt2_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500.xsett" "%SM%\split_milp_m2eic_sqt2.xconf" "output_mu500_sqt2_s0" 0 >> run_sqt2_check_runs.log 2>&1
if errorlevel 1 (echo SQT2-MU500 seed0 FAIL >> run_sqt2_check.log) else (echo SQT2-MU500 seed0 SUCCESS >> run_sqt2_check.log)
echo SQT2-CHECK-DONE >> run_sqt2_check.log
