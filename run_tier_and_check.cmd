@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo CLOSE-DRIFTCHECK6 seed0 START >> run_tier_and_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_close_driftcheck6_s0" 0 >> run_tier_and_check_runs.log 2>&1
if errorlevel 1 (echo CLOSE-DRIFTCHECK6 seed0 FAIL >> run_tier_and_check.log) else (echo CLOSE-DRIFTCHECK6 seed0 SUCCESS >> run_tier_and_check.log)
echo TIER-2H seed0 START >> run_tier_and_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_tier_2h_s0" 0 >> run_tier_and_check_runs.log 2>&1
if errorlevel 1 (echo TIER-2H seed0 FAIL >> run_tier_and_check.log) else (echo TIER-2H seed0 SUCCESS >> run_tier_and_check.log)
echo TIER-CHECK-DONE >> run_tier_and_check.log
