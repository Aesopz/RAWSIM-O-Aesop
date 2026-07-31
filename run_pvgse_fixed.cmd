@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%C in (pvgs_e pvgs_e_eps) do (
  echo %%C-FIXED-S0 START >> run_pvgse_fixed.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\fixed_1150.xsett" "%SM%\%%C.xconf" "output_%%C_fixed_s0" 0 >> run_pvgse_fixed_runs.log 2>&1
  if errorlevel 1 (echo %%C-FIXED-S0 FAIL >> run_pvgse_fixed.log) else (echo %%C-FIXED-S0 SUCCESS >> run_pvgse_fixed.log)
)
echo PVGSE-FIXED-DONE >> run_pvgse_fixed.log
