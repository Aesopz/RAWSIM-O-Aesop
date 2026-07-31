@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (0 1) do (
  echo M1G-FX1150-S%%S START >> run_m1g_fixed1150.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\fixed_1150.xsett" "%SM%\m1g.xconf" "output_fxv_m1g_s%%S" %%S >> run_m1g_fixed1150_runs.log 2>&1
  if errorlevel 1 (echo M1G-FX1150-S%%S FAIL >> run_m1g_fixed1150.log) else (echo M1G-FX1150-S%%S SUCCESS >> run_m1g_fixed1150.log)
)
echo M1G-FX1150-DONE >> run_m1g_fixed1150.log
