@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (0 1) do (
  echo M1G-FIXED3000-S%%S START >> run_fixed3000.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\fixed_3000.xsett" "%SM%\m1g.xconf" "output_fixed3000_m1g_s%%S" %%S >> run_fixed3000_runs.log 2>&1
  if errorlevel 1 (echo M1G-FIXED3000-S%%S FAIL >> run_fixed3000.log) else (echo M1G-FIXED3000-S%%S SUCCESS >> run_fixed3000.log)
  echo TIER-FIXED3000-S%%S START >> run_fixed3000.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\fixed_3000.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_fixed3000_tier_s%%S" %%S >> run_fixed3000_runs.log 2>&1
  if errorlevel 1 (echo TIER-FIXED3000-S%%S FAIL >> run_fixed3000.log) else (echo TIER-FIXED3000-S%%S SUCCESS >> run_fixed3000.log)
)
echo FIXED3000-DONE >> run_fixed3000.log
