@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo FAIR-H2H START > run_fair.log
for %%C in (split_milp_m3g hgs_m3) do (
  "%CLI%" "%SM%\small.xlayo" "%SM%\fixed_fill1350_inv70.xsett" "%SM%\%%C.xconf" "output_fair_%%C" 0 >> run_fair_runs.log 2>&1
  if errorlevel 1 (echo %%C FAIL >> run_fair.log) else (echo %%C SUCCESS >> run_fair.log)
)
echo FAIR-H2H-DONE >> run_fair.log
