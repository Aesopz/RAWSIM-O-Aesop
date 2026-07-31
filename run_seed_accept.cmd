@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo SEED-ACCEPT START > run_seed_accept.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv70.xsett" "%SM%\split_milp_m3g_seed.xconf" "output_seed_s1" 1 >> run_seed_accept_runs.log 2>&1
if errorlevel 1 (echo seed-S1 FAIL >> run_seed_accept.log) else (echo seed-S1 SUCCESS >> run_seed_accept.log)
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o200_mu100_4h_inv70.xsett" "%SM%\split_milp_m3g_seed.xconf" "output_seed_o200_s0" 0 >> run_seed_accept_runs.log 2>&1
if errorlevel 1 (echo seed-o200 FAIL >> run_seed_accept.log) else (echo seed-o200 SUCCESS >> run_seed_accept.log)
echo SEED-ACCEPT-DONE >> run_seed_accept.log
