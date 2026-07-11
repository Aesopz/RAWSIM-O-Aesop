@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett

echo [%TIME%] Starting M0 (m1g) > run_small_late_check.log
"%CLI%" "%LAYO%" "%SETT%" "Material\Instances\CoreBenchmark\small\m1g.xconf" "output_small_late_m0" 0 >> run_small_late_check.log 2>&1
echo [%TIME%] Finished M0 >> run_small_late_check.log

echo [%TIME%] Starting M1 (split_milp_m1) >> run_small_late_check.log
"%CLI%" "%LAYO%" "%SETT%" "Material\Instances\CoreBenchmark\small\split_milp_m1.xconf" "output_small_late_m1" 0 >> run_small_late_check.log 2>&1
echo [%TIME%] Finished M1 >> run_small_late_check.log

echo [%TIME%] Starting M2 (split_milp_m2) >> run_small_late_check.log
"%CLI%" "%LAYO%" "%SETT%" "Material\Instances\CoreBenchmark\small\split_milp_m2.xconf" "output_small_late_m2" 0 >> run_small_late_check.log 2>&1
echo [%TIME%] Finished M2 >> run_small_late_check.log

echo [%TIME%] ALL DONE >> run_small_late_check.log
