@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo DRIFT-NOCAP-100-S0 START >> run_splitdrive_test.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\split_milp_m2eic_nocap.xconf" "output_drift_nocap100_s0" 0 >> run_splitdrive_test_runs.log 2>&1
if errorlevel 1 (echo DRIFT-NOCAP-100-S0 FAIL >> run_splitdrive_test.log) else (echo DRIFT-NOCAP-100-S0 SUCCESS >> run_splitdrive_test.log)
for %%S in (0 1 2 3 4) do (
  echo SPLITDRIVE-MU500-4H-S%%S START >> run_splitdrive_test.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\split_milp_m2eic_splitdrive.xconf" "output_splitdrive_mu500_s%%S" %%S >> run_splitdrive_test_runs.log 2>&1
  if errorlevel 1 (echo SPLITDRIVE-MU500-4H-S%%S FAIL >> run_splitdrive_test.log) else (echo SPLITDRIVE-MU500-4H-S%%S SUCCESS >> run_splitdrive_test.log)
)
echo SPLITDRIVE-TEST-DONE >> run_splitdrive_test.log
