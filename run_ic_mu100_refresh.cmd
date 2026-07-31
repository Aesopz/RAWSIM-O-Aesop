@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo IC-MU100-REFRESH START >> run_ic_mu100_refresh.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_mu100_ic_refresh_s0" 0 >> run_ic_mu100_refresh_runs.log 2>&1
if errorlevel 1 (echo IC-MU100-REFRESH FAIL >> run_ic_mu100_refresh.log) else (echo IC-MU100-REFRESH SUCCESS >> run_ic_mu100_refresh.log)
echo IC-MU100-REFRESH-DONE >> run_ic_mu100_refresh.log
