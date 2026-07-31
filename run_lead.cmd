@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo LEAD START > run_lead.log
for %%S in (0 1) do (
  for %%C in (split_milp_m3g split_milp_m3g_lead100) do (
    "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv70.xsett" "%SM%\%%C.xconf" "output_ld_%%C_s%%S" %%S >> run_lead_runs.log 2>&1
    if errorlevel 1 (echo %%C-S%%S FAIL >> run_lead.log) else (echo %%C-S%%S SUCCESS >> run_lead.log)
  )
)
echo LEAD-DONE >> run_lead.log
