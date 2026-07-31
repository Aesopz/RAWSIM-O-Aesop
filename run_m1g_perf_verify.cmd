@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo M1G-CORRECTNESS-S0 START >> run_m1g_perf_verify.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\m1g.xconf" "output_m1g_perf_verify_s0" 0 >> run_m1g_perf_verify_runs.log 2>&1
if errorlevel 1 (echo M1G-CORRECTNESS-S0 FAIL >> run_m1g_perf_verify.log) else (echo M1G-CORRECTNESS-S0 SUCCESS >> run_m1g_perf_verify.log)
echo M1G-PERF-VERIFY-DONE >> run_m1g_perf_verify.log
