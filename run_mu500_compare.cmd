@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo M1G-MU500-S0 START >> run_mu500_compare.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500.xsett" "%SM%\m1g.xconf" "output_mu500_m1g_s0" 0 >> run_mu500_compare_runs.log 2>&1
if errorlevel 1 (echo M1G-MU500-S0 FAIL >> run_mu500_compare.log) else (echo M1G-MU500-S0 SUCCESS >> run_mu500_compare.log)
echo IC-MU500-S0 START >> run_mu500_compare.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_mu500_ic_s0" 0 >> run_mu500_compare_runs.log 2>&1
if errorlevel 1 (echo IC-MU500-S0 FAIL >> run_mu500_compare.log) else (echo IC-MU500-S0 SUCCESS >> run_mu500_compare.log)
echo MU500-DONE >> run_mu500_compare.log
