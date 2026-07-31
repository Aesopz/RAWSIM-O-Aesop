@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
set LG=Material\Instances\CoreBenchmark\large
set LAYO=%LG%\large_test-toobig-45bot.xlayo
set SETT=%LG%\large_test.xsett
echo LARGE START %DATE% %TIME% > run_large.log
for %%S in (0 1) do (
  for %%C in (hgs_m3 hadgs_aligned) do (
    echo %%C-S%%S START >> run_large.log
    "%CLI%" "%LAYO%" "%SETT%" "%SM%\%%C.xconf" "output_lg_%%C_s%%S" %%S >> run_large_runs.log 2>&1
    if errorlevel 1 (echo %%C-S%%S FAIL >> run_large.log) else (echo %%C-S%%S SUCCESS >> run_large.log)
  )
)
echo LARGE-DONE >> run_large.log
