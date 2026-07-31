@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (0 1 2 3 4) do (
  echo W3K-MU500INV50-4H-S%%S START >> run_w3k_deadlock_test.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\split_milp_m2eic_nocap_w3k.xconf" "output_w3k_mu500_s%%S" %%S >> run_w3k_deadlock_test_runs.log 2>&1
  if errorlevel 1 (echo W3K-MU500INV50-4H-S%%S FAIL >> run_w3k_deadlock_test.log) else (echo W3K-MU500INV50-4H-S%%S SUCCESS >> run_w3k_deadlock_test.log)
)
echo W3K-DEADLOCK-TEST-DONE >> run_w3k_deadlock_test.log
