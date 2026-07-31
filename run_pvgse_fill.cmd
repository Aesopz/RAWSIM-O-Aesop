@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%C in (pvgs_e_eps pvgs_e_eps_tier) do (
  for %%S in (0 1) do (
    echo %%C-FILL-S%%S START >> run_pvgse_fill.log
    "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\%%C.xconf" "output_%%C_fill_s%%S" %%S >> run_pvgse_fill_runs.log 2>&1
    if errorlevel 1 (echo %%C-FILL-S%%S FAIL >> run_pvgse_fill.log) else (echo %%C-FILL-S%%S SUCCESS >> run_pvgse_fill.log)
  )
)
echo PVGSE-FILL-DONE >> run_pvgse_fill.log
