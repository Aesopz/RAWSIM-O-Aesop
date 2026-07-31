@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
set LAYO=%SM%\small.xlayo
set SETT=%SM%\small_o100_mu100_4h_inv70.xsett
echo LB-PROBE START %DATE% %TIME% > run_lb_probe.log
for %%S in (0 1) do (
  for %%C in (m1e m1elb6 m1elb12) do (
    echo %%C-S%%S START %TIME% >> run_lb_probe.log
    "%CLI%" "%LAYO%" "%SETT%" "%SM%\split_milp_%%C.xconf" "output_lb_%%C_s%%S" %%S >> run_lb_probe_runs.log 2>&1
    if errorlevel 1 (echo %%C-S%%S FAIL >> run_lb_probe.log) else (echo %%C-S%%S SUCCESS >> run_lb_probe.log)
  )
)
echo LB-PROBE-DONE %TIME% >> run_lb_probe.log
