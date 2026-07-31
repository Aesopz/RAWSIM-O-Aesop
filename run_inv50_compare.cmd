@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo M1G-INV50-S0 START >> run_inv50_compare.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_inv50.xsett" "%SM%\m1g.xconf" "output_inv50_m1g_s0" 0 >> run_inv50_compare_runs.log 2>&1
if errorlevel 1 (echo M1G-INV50-S0 FAIL >> run_inv50_compare.log) else (echo M1G-INV50-S0 SUCCESS >> run_inv50_compare.log)
echo IC-INV50-S0 START >> run_inv50_compare.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_inv50.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_inv50_ic_s0" 0 >> run_inv50_compare_runs.log 2>&1
if errorlevel 1 (echo IC-INV50-S0 FAIL >> run_inv50_compare.log) else (echo IC-INV50-S0 SUCCESS >> run_inv50_compare.log)
echo INV50-DONE >> run_inv50_compare.log
