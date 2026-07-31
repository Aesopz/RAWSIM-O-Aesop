@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo FIXED-H2H START > run_fixed.log
for %%S in (0 1) do (
  for %%C in (split_milp_m3g hgs_m3) do (
    "%CLI%" "%SM%\small.xlayo" "%SM%\fixed_1150_inv70.xsett" "%SM%\%%C.xconf" "output_fx_%%C_s%%S" %%S >> run_fixed_runs.log 2>&1
    if errorlevel 1 (echo %%C-S%%S FAIL >> run_fixed.log) else (echo %%C-S%%S SUCCESS >> run_fixed.log)
  )
)
echo FIXED-H2H-DONE >> run_fixed.log
