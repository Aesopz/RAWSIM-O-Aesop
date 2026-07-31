@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo 4O10B-CLOSE-S0 START >> run_4o10b_tier.log
"%CLI%" "%SM%\small_4o10b.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_4o10b_close_s0" 0 >> run_4o10b_tier_runs.log 2>&1
if errorlevel 1 (echo 4O10B-CLOSE-S0 FAIL >> run_4o10b_tier.log) else (echo 4O10B-CLOSE-S0 SUCCESS >> run_4o10b_tier.log)
echo 4O10B-TIER-S0 START >> run_4o10b_tier.log
"%CLI%" "%SM%\small_4o10b.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_4o10b_tier_s0" 0 >> run_4o10b_tier_runs.log 2>&1
if errorlevel 1 (echo 4O10B-TIER-S0 FAIL >> run_4o10b_tier.log) else (echo 4O10B-TIER-S0 SUCCESS >> run_4o10b_tier.log)
echo 4O10B-TIER-DONE >> run_4o10b_tier.log
