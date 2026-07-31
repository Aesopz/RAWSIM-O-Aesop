@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo TW-2H seed0 START >> run_tw_2h.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tw.xconf" "output_tw_2h_s0" 0 >> run_tw_2h_runs.log 2>&1
if errorlevel 1 (echo TW-2H seed0 FAIL >> run_tw_2h.log) else (echo TW-2H seed0 SUCCESS >> run_tw_2h.log)
echo TW-2H-DONE >> run_tw_2h.log
