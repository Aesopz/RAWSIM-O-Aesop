@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo TIER-DRIFTCHECK seed0 START >> run_sq_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_tier_driftcheck_s0" 0 >> run_sq_check_runs.log 2>&1
if errorlevel 1 (echo TIER-DRIFTCHECK seed0 FAIL >> run_sq_check.log) else (echo TIER-DRIFTCHECK seed0 SUCCESS >> run_sq_check.log)
echo SQ-MU500 seed0 START >> run_sq_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500.xsett" "%SM%\split_milp_m2eic_sq.xconf" "output_mu500_sq_s0" 0 >> run_sq_check_runs.log 2>&1
if errorlevel 1 (echo SQ-MU500 seed0 FAIL >> run_sq_check.log) else (echo SQ-MU500 seed0 SUCCESS >> run_sq_check.log)
echo SQ-CHECK-DONE >> run_sq_check.log
