@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo TIERA-2H seed0 START >> run_tier_sweep.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tierA.xconf" "output_tierA_2h_s0" 0 >> run_tier_sweep_runs.log 2>&1
if errorlevel 1 (echo TIERA-2H seed0 FAIL >> run_tier_sweep.log) else (echo TIERA-2H seed0 SUCCESS >> run_tier_sweep.log)
echo TIERB-2H seed0 START >> run_tier_sweep.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tierB.xconf" "output_tierB_2h_s0" 0 >> run_tier_sweep_runs.log 2>&1
if errorlevel 1 (echo TIERB-2H seed0 FAIL >> run_tier_sweep.log) else (echo TIERB-2H seed0 SUCCESS >> run_tier_sweep.log)
echo TIERC-2H seed0 START >> run_tier_sweep.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tierC.xconf" "output_tierC_2h_s0" 0 >> run_tier_sweep_runs.log 2>&1
if errorlevel 1 (echo TIERC-2H seed0 FAIL >> run_tier_sweep.log) else (echo TIERC-2H seed0 SUCCESS >> run_tier_sweep.log)
echo TIERD-2H seed0 START >> run_tier_sweep.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tierD.xconf" "output_tierD_2h_s0" 0 >> run_tier_sweep_runs.log 2>&1
if errorlevel 1 (echo TIERD-2H seed0 FAIL >> run_tier_sweep.log) else (echo TIERD-2H seed0 SUCCESS >> run_tier_sweep.log)
echo TIERE-2H seed0 START >> run_tier_sweep.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tierE.xconf" "output_tierE_2h_s0" 0 >> run_tier_sweep_runs.log 2>&1
if errorlevel 1 (echo TIERE-2H seed0 FAIL >> run_tier_sweep.log) else (echo TIERE-2H seed0 SUCCESS >> run_tier_sweep.log)
echo TIER-SWEEP-DONE >> run_tier_sweep.log
