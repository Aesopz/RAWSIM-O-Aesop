@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (0 1 2 3 4) do (
  echo M1G-MU500INV50-4H-S%%S START >> run_mu500_inv50_4h_5seed.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\m1g.xconf" "output_mu500inv50_4h_m1g_s%%S" %%S >> run_mu500_inv50_4h_5seed_runs.log 2>&1
  if errorlevel 1 (echo M1G-MU500INV50-4H-S%%S FAIL >> run_mu500_inv50_4h_5seed.log) else (echo M1G-MU500INV50-4H-S%%S SUCCESS >> run_mu500_inv50_4h_5seed.log)
  echo NOCAP-MU500INV50-4H-S%%S START >> run_mu500_inv50_4h_5seed.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\split_milp_m2eic_nocap.xconf" "output_mu500inv50_4h_nocap_s%%S" %%S >> run_mu500_inv50_4h_5seed_runs.log 2>&1
  if errorlevel 1 (echo NOCAP-MU500INV50-4H-S%%S FAIL >> run_mu500_inv50_4h_5seed.log) else (echo NOCAP-MU500INV50-4H-S%%S SUCCESS >> run_mu500_inv50_4h_5seed.log)
)
echo MU500INV50-4H-5SEED-DONE >> run_mu500_inv50_4h_5seed.log
