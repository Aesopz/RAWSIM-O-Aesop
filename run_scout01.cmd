@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo SCOUT01-2H seed0 START >> run_scout01.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_scout01.xconf" "output_scout01_2h_s0" 0 >> run_scout01_runs.log 2>&1
if errorlevel 1 (echo SCOUT01-2H seed0 FAIL >> run_scout01.log) else (echo SCOUT01-2H seed0 SUCCESS >> run_scout01.log)
echo SCOUT01-DONE >> run_scout01.log
