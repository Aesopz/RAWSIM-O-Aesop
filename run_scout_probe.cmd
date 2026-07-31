@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo SCOUT-2H seed0 START >> run_scout_probe.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_scout.xconf" "output_scout_2h_s0" 0 >> run_scout_probe_runs.log 2>&1
if errorlevel 1 (echo SCOUT-2H seed0 FAIL >> run_scout_probe.log) else (echo SCOUT-2H seed0 SUCCESS >> run_scout_probe.log)
echo SCOUT-PROBE-DONE >> run_scout_probe.log
