@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo M1G-8H seed0 START >> run_8h_m1g_ic.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_8h.xsett" "%SM%\m1g.xconf" "output_8h_m1g_s0" 0 >> run_8h_m1g_ic_runs.log 2>&1
if errorlevel 1 (echo M1G-8H seed0 FAIL >> run_8h_m1g_ic.log) else (echo M1G-8H seed0 SUCCESS >> run_8h_m1g_ic.log)
echo IC-CLOSE-8H seed0 START >> run_8h_m1g_ic.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_8h.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_8h_icclose_s0" 0 >> run_8h_m1g_ic_runs.log 2>&1
if errorlevel 1 (echo IC-CLOSE-8H seed0 FAIL >> run_8h_m1g_ic.log) else (echo IC-CLOSE-8H seed0 SUCCESS >> run_8h_m1g_ic.log)
echo 8H-BATCH-DONE >> run_8h_m1g_ic.log
