@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (0 1) do (
  echo TIER-FX-S%%S START >> run_fixed_v5verify.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\fixed_1150.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_fxv_tier_s%%S" %%S >> run_fixed_v5verify_runs.log 2>&1
  if errorlevel 1 (echo TIER-FX-S%%S FAIL >> run_fixed_v5verify.log) else (echo TIER-FX-S%%S SUCCESS >> run_fixed_v5verify.log)
  echo V5-FX-S%%S START >> run_fixed_v5verify.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\fixed_1150.xsett" "%SM%\split_milp_m2eic_v5.xconf" "output_fxv_v5_s%%S" %%S >> run_fixed_v5verify_runs.log 2>&1
  if errorlevel 1 (echo V5-FX-S%%S FAIL >> run_fixed_v5verify.log) else (echo V5-FX-S%%S SUCCESS >> run_fixed_v5verify.log)
)
echo FIXED-V5VERIFY-DONE >> run_fixed_v5verify.log
