@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo TIER-DRIFTCHECK4 seed0 START >> run_nocap_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_tier_driftcheck4_s0" 0 >> run_nocap_check_runs.log 2>&1
if errorlevel 1 (echo TIER-DRIFTCHECK4 seed0 FAIL >> run_nocap_check.log) else (echo TIER-DRIFTCHECK4 seed0 SUCCESS >> run_nocap_check.log)
echo NOCAP-MU100 seed0 START >> run_nocap_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_nocap.xconf" "output_mu100_nocap_s0" 0 >> run_nocap_check_runs.log 2>&1
if errorlevel 1 (echo NOCAP-MU100 seed0 FAIL >> run_nocap_check.log) else (echo NOCAP-MU100 seed0 SUCCESS >> run_nocap_check.log)
echo NOCAP-MU500 seed0 START >> run_nocap_check.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500.xsett" "%SM%\split_milp_m2eic_nocap.xconf" "output_mu500_nocap_s0" 0 >> run_nocap_check_runs.log 2>&1
if errorlevel 1 (echo NOCAP-MU500 seed0 FAIL >> run_nocap_check.log) else (echo NOCAP-MU500 seed0 SUCCESS >> run_nocap_check.log)
echo NOCAP-CHECK-DONE >> run_nocap_check.log
