@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
set SETT=%SM%\fixed_fill1350_inv70.xsett
echo DUALPROBE START > run_dualprobe.log
"%CLI%" "%SM%\small.xlayo" "%SETT%" "%SM%\split_milp_m3g.xconf" "output_bitid_m3g" 0 >> run_dualprobe_runs.log 2>&1
if errorlevel 1 (echo bitid FAIL >> run_dualprobe.log) else (echo bitid SUCCESS >> run_dualprobe.log)
"%CLI%" "%SM%\small.xlayo" "%SETT%" "%SM%\split_milp_m3g_probe.xconf" "output_probe_m3g" 0 >> run_dualprobe_runs.log 2>&1
if errorlevel 1 (echo probe FAIL >> run_dualprobe.log) else (echo probe SUCCESS >> run_dualprobe.log)
echo DUALPROBE-DONE >> run_dualprobe.log
