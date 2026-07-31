@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (1 2 3 4) do (
  echo CLOSE-S%%S START >> run_tier_5seed.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_5s_close_s%%S" %%S >> run_tier_5seed_runs.log 2>&1
  if errorlevel 1 (echo CLOSE-S%%S FAIL >> run_tier_5seed.log) else (echo CLOSE-S%%S SUCCESS >> run_tier_5seed.log)
  echo TIER-S%%S START >> run_tier_5seed.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_5s_tier_s%%S" %%S >> run_tier_5seed_runs.log 2>&1
  if errorlevel 1 (echo TIER-S%%S FAIL >> run_tier_5seed.log) else (echo TIER-S%%S SUCCESS >> run_tier_5seed.log)
)
echo TIER-5SEED-DONE >> run_tier_5seed.log
