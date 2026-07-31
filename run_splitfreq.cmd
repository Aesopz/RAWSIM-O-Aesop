@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo SF START > run_splitfreq.log
for %%C in (split_milp_m3g hgs_m3) do (
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv70.xsett" "%SM%\%%C.xconf" "output_sf_%%C" 0 >> run_splitfreq_runs.log 2>&1
  if errorlevel 1 (echo %%C FAIL >> run_splitfreq.log) else (echo %%C SUCCESS >> run_splitfreq.log)
)
echo SF-DONE >> run_splitfreq.log
