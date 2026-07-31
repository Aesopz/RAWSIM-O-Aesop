@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo ICFIX-2H seed0 START >> run_icfix_2h8h.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_icfix_2h_s0" 0 >> run_icfix_2h8h_runs.log 2>&1
if errorlevel 1 (echo ICFIX-2H seed0 FAIL >> run_icfix_2h8h.log) else (echo ICFIX-2H seed0 SUCCESS >> run_icfix_2h8h.log)
echo ICFIX-8H seed0 START >> run_icfix_2h8h.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_8h.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_icfix_8h_s0" 0 >> run_icfix_2h8h_runs.log 2>&1
if errorlevel 1 (echo ICFIX-8H seed0 FAIL >> run_icfix_2h8h.log) else (echo ICFIX-8H seed0 SUCCESS >> run_icfix_2h8h.log)
echo ICFIX-DONE >> run_icfix_2h8h.log
