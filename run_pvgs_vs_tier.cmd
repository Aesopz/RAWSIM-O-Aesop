@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
rem --- PVGS on Fill (500-SKU 4h) ---
for %%S in (0 1) do (
  echo PVGS-FILL-S%%S START >> run_pvgs_vs_tier.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\pvgs_m2e.xconf" "output_pvgs_fill_s%%S" %%S >> run_pvgs_vs_tier_runs.log 2>&1
  if errorlevel 1 (echo PVGS-FILL-S%%S FAIL >> run_pvgs_vs_tier.log) else (echo PVGS-FILL-S%%S SUCCESS >> run_pvgs_vs_tier.log)
)
rem --- PVGS on Fixed (identical 1150-order file) ---
for %%S in (0 1) do (
  echo PVGS-FIXED-S%%S START >> run_pvgs_vs_tier.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\fixed_1150.xsett" "%SM%\pvgs_m2e.xconf" "output_pvgs_fixed_s%%S" %%S >> run_pvgs_vs_tier_runs.log 2>&1
  if errorlevel 1 (echo PVGS-FIXED-S%%S FAIL >> run_pvgs_vs_tier.log) else (echo PVGS-FIXED-S%%S SUCCESS >> run_pvgs_vs_tier.log)
)
echo PVGS-VS-TIER-DONE >> run_pvgs_vs_tier.log
