@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (0 1) do (
  echo TIERNEW-mu500-S%%S START >> run_tier_w3_mu500only.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_tiernew_mu500_s%%S" %%S >> run_tier_w3_mu500only_runs.log 2>&1
  if errorlevel 1 (echo TIERNEW-mu500-S%%S FAIL >> run_tier_w3_mu500only.log) else (echo TIERNEW-mu500-S%%S SUCCESS >> run_tier_w3_mu500only.log)
  echo TIEROLD-mu500-S%%S START >> run_tier_w3_mu500only.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\split_milp_m2eic_tier_w3_5.xconf" "output_tierold_mu500_s%%S" %%S >> run_tier_w3_mu500only_runs.log 2>&1
  if errorlevel 1 (echo TIEROLD-mu500-S%%S FAIL >> run_tier_w3_mu500only.log) else (echo TIEROLD-mu500-S%%S SUCCESS >> run_tier_w3_mu500only.log)
)
echo TIER-MU500-DONE >> run_tier_w3_mu500only.log
