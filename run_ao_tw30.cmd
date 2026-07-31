@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo AO-2H seed0 START >> run_ao_tw30.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_ao.xconf" "output_ao_2h_s0" 0 >> run_ao_tw30_runs.log 2>&1
if errorlevel 1 (echo AO-2H seed0 FAIL >> run_ao_tw30.log) else (echo AO-2H seed0 SUCCESS >> run_ao_tw30.log)
echo TW30-2H seed0 START >> run_ao_tw30.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_tw30.xconf" "output_tw30_2h_s0" 0 >> run_ao_tw30_runs.log 2>&1
if errorlevel 1 (echo TW30-2H seed0 FAIL >> run_ao_tw30.log) else (echo TW30-2H seed0 SUCCESS >> run_ao_tw30.log)
echo AO-TW30-DONE >> run_ao_tw30.log
