@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (1 2 3 4) do (
  echo 4O10B-CLOSE-S%%S START >> run_4o10b_5seed.log
  "%CLI%" "%SM%\small_4o10b.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_4o10b_close_s%%S" %%S >> run_4o10b_5seed_runs.log 2>&1
  if errorlevel 1 (echo 4O10B-CLOSE-S%%S FAIL >> run_4o10b_5seed.log) else (echo 4O10B-CLOSE-S%%S SUCCESS >> run_4o10b_5seed.log)
  echo 4O10B-TIER-S%%S START >> run_4o10b_5seed.log
  "%CLI%" "%SM%\small_4o10b.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_4o10b_tier_s%%S" %%S >> run_4o10b_5seed_runs.log 2>&1
  if errorlevel 1 (echo 4O10B-TIER-S%%S FAIL >> run_4o10b_5seed.log) else (echo 4O10B-TIER-S%%S SUCCESS >> run_4o10b_5seed.log)
)
echo 4O10B-5SEED-DONE >> run_4o10b_5seed.log
