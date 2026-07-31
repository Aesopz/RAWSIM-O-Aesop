@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
set LAYO=%SM%\small.xlayo
set SETT=%SM%\fixed_3000.xsett
echo HGS-HADGS START %DATE% %TIME% > run_hgs_vs_hadgs.log
for %%S in (0 1) do (
  for %%C in (hgs_m3 hadgs_aligned split_milp_m3g) do (
    echo %%C-S%%S START %TIME% >> run_hgs_vs_hadgs.log
    "%CLI%" "%LAYO%" "%SETT%" "%SM%\%%C.xconf" "output_big_%%C_s%%S" %%S >> run_hgs_vs_hadgs_runs.log 2>&1
    if errorlevel 1 (echo %%C-S%%S FAIL >> run_hgs_vs_hadgs.log) else (echo %%C-S%%S SUCCESS >> run_hgs_vs_hadgs.log)
  )
)
echo HGS-HADGS-DONE %TIME% >> run_hgs_vs_hadgs.log
