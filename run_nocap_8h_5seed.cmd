@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (0 1 2 3 4) do (
  echo M1G-8H-S%%S START >> run_nocap_8h_5seed.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_8h.xsett" "%SM%\m1g.xconf" "output_8h5s_m1g_s%%S" %%S >> run_nocap_8h_5seed_runs.log 2>&1
  if errorlevel 1 (echo M1G-8H-S%%S FAIL >> run_nocap_8h_5seed.log) else (echo M1G-8H-S%%S SUCCESS >> run_nocap_8h_5seed.log)
  echo NOCAP-8H-S%%S START >> run_nocap_8h_5seed.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_8h.xsett" "%SM%\split_milp_m2eic_nocap.xconf" "output_8h5s_nocap_s%%S" %%S >> run_nocap_8h_5seed_runs.log 2>&1
  if errorlevel 1 (echo NOCAP-8H-S%%S FAIL >> run_nocap_8h_5seed.log) else (echo NOCAP-8H-S%%S SUCCESS >> run_nocap_8h_5seed.log)
)
echo NOCAP-8H-5SEED-DONE >> run_nocap_8h_5seed.log
