@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%K in (mu100 mu500) do (
  for %%S in (0 1 2 3 4) do (
    echo TIERNEW-%%K-S%%S START >> run_tier_w3_compare.log
    "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_%%K_4h_inv50.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_tiernew_%%K_s%%S" %%S >> run_tier_w3_compare_runs.log 2>&1
    if errorlevel 1 (echo TIERNEW-%%K-S%%S FAIL >> run_tier_w3_compare.log) else (echo TIERNEW-%%K-S%%S SUCCESS >> run_tier_w3_compare.log)
    echo TIEROLD-%%K-S%%S START >> run_tier_w3_compare.log
    "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_%%K_4h_inv50.xsett" "%SM%\split_milp_m2eic_tier_w3_5.xconf" "output_tierold_%%K_s%%S" %%S >> run_tier_w3_compare_runs.log 2>&1
    if errorlevel 1 (echo TIEROLD-%%K-S%%S FAIL >> run_tier_w3_compare.log) else (echo TIEROLD-%%K-S%%S SUCCESS >> run_tier_w3_compare.log)
  )
)
echo TIER-W3-COMPARE-DONE >> run_tier_w3_compare.log
