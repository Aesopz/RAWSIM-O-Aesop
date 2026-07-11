@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett
set SCRATCH=C:\Users\Aesop\AppData\Local\Temp\claude\C--Users-Aesop-Desktop-EE-RAWSim-O-PP\aaefd475-0310-4ad0-ae40-4d218c9cdf83\scratchpad

echo [%TIME%] Starting DiagA (theta=999, no new partial children) > run_pvgs_diag.log
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\pvgs_m2e_diagA.xconf" "output_pvgs_diagA" 0 >> run_pvgs_diag.log 2>&1
echo [%TIME%] Finished DiagA >> run_pvgs_diag.log

echo [%TIME%] Starting DiagB (partialWeight=0, parentBonus=0) >> run_pvgs_diag.log
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\pvgs_m2e_diagB.xconf" "output_pvgs_diagB" 0 >> run_pvgs_diag.log 2>&1
echo [%TIME%] Finished DiagB >> run_pvgs_diag.log

echo [%TIME%] ALL DONE >> run_pvgs_diag.log
