@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (2 3 4) do (
  echo TIER-S%%S START >> run_cov5_verify.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_tiernew_mu500_s%%S" %%S >> run_cov5_verify_runs.log 2>&1
  if errorlevel 1 (echo TIER-S%%S FAIL >> run_cov5_verify.log) else (echo TIER-S%%S SUCCESS >> run_cov5_verify.log)
  echo COV5-S%%S START >> run_cov5_verify.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\split_milp_m2eic_C_cov5.xconf" "output_covdrive_C_cov5_s%%S" %%S >> run_cov5_verify_runs.log 2>&1
  if errorlevel 1 (echo COV5-S%%S FAIL >> run_cov5_verify.log) else (echo COV5-S%%S SUCCESS >> run_cov5_verify.log)
)
echo COV5-VERIFY-DONE >> run_cov5_verify.log
