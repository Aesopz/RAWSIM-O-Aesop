@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo M1G-4O10B-8H-S1 START >> run_m1g_speed_4o10b.log
"%CLI%" "%SM%\small_4o10b.xlayo" "%SM%\small_o100_mu100_8h.xsett" "%SM%\m1g.xconf" "output_4oi2_m1g_perf_s1" 1 >> run_m1g_speed_4o10b_runs.log 2>&1
if errorlevel 1 (echo M1G-4O10B-8H-S1 FAIL >> run_m1g_speed_4o10b.log) else (echo M1G-4O10B-8H-S1 SUCCESS >> run_m1g_speed_4o10b.log)
echo M1G-SPEED-DONE >> run_m1g_speed_4o10b.log
