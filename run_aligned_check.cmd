@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett
set DIR=Material\Instances\CoreBenchmark\small

echo [%TIME%] Regression m1e (default w3=1000, expect 649) > run_aligned_check.log
"%CLI%" "%LAYO%" "%SETT%" "%DIR%\split_milp_m1e.xconf" "output_regress_m1e_w4" 0 >> run_aligned_check.log 2>&1
echo [%TIME%] Regression m2e (default, expect 643) >> run_aligned_check.log
"%CLI%" "%LAYO%" "%SETT%" "%DIR%\split_milp_m2e.xconf" "output_regress_m2e_w4" 0 >> run_aligned_check.log 2>&1
echo [%TIME%] Aligned m1ea (w3=0) >> run_aligned_check.log
"%CLI%" "%LAYO%" "%SETT%" "%DIR%\split_milp_m1ea.xconf" "output_aligned_m1ea" 0 >> run_aligned_check.log 2>&1
echo [%TIME%] Aligned m2ea (w3=0) >> run_aligned_check.log
"%CLI%" "%LAYO%" "%SETT%" "%DIR%\split_milp_m2ea.xconf" "output_aligned_m2ea" 0 >> run_aligned_check.log 2>&1
echo [%TIME%] ALL DONE >> run_aligned_check.log
