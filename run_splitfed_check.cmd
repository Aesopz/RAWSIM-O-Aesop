@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo TIER-DRIFTCHECK3 seed0 START >> run_splitfed_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_tier_driftcheck3_s0" 0 >> run_splitfed_check_runs.log 2>&1
if errorlevel 1 (echo TIER-DRIFTCHECK3 seed0 FAIL >> run_splitfed_check.log) else (echo TIER-DRIFTCHECK3 seed0 SUCCESS >> run_splitfed_check.log)
echo SPLITFED-MU500 seed0 START >> run_splitfed_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500.xsett" "%SM%\split_milp_m2eic_splitfed.xconf" "output_mu500_splitfed_s0" 0 >> run_splitfed_check_runs.log 2>&1
if errorlevel 1 (echo SPLITFED-MU500 seed0 FAIL >> run_splitfed_check.log) else (echo SPLITFED-MU500 seed0 SUCCESS >> run_splitfed_check.log)
echo SPLITFED-CHECK-DONE >> run_splitfed_check.log
