@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%A in (D_cov8 E_cov12 F_cov20) do (
  for %%S in (0 1) do (
    echo %%A-S%%S START >> run_covsweep.log
    "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\split_milp_m2eic_%%A.xconf" "output_covsweep_%%A_s%%S" %%S >> run_covsweep_runs.log 2>&1
    if errorlevel 1 (echo %%A-S%%S FAIL >> run_covsweep.log) else (echo %%A-S%%S SUCCESS >> run_covsweep.log)
  )
)
echo COVSWEEP-DONE >> run_covsweep.log
