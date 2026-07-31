@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo L40 START > run_lead40.log
for %%S in (0 1) do (
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv70.xsett" "%SM%\split_milp_m3g_lead40.xconf" "output_ld_split_milp_m3g_lead40_s%%S" %%S >> run_lead40_runs.log 2>&1
  if errorlevel 1 (echo lead40-S%%S FAIL >> run_lead40.log) else (echo lead40-S%%S SUCCESS >> run_lead40.log)
)
echo L40-DONE >> run_lead40.log
