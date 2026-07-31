@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo HH-PVGSE seed0 START >> run_hh_rerun.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\pvgs_e.xconf" "output_hh_pvgse_s0" 0 >> run_hh_rerun_runs.log 2>&1
if errorlevel 1 (echo HH-PVGSE seed0 FAIL >> run_hh_rerun.log) else (echo HH-PVGSE seed0 SUCCESS >> run_hh_rerun.log)
echo HH-ICCLOSE seed0 START >> run_hh_rerun.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_hh_icclose_s0" 0 >> run_hh_rerun_runs.log 2>&1
if errorlevel 1 (echo HH-ICCLOSE seed0 FAIL >> run_hh_rerun.log) else (echo HH-ICCLOSE seed0 SUCCESS >> run_hh_rerun.log)
echo HH-RERUN-DONE >> run_hh_rerun.log
