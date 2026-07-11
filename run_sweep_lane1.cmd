@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett
set SCRATCH=C:\Users\Aesop\AppData\Local\Temp\claude\C--Users-Aesop-Desktop-EE-RAWSim-O-PP\aaefd475-0310-4ad0-ae40-4d218c9cdf83\scratchpad
echo START > run_sweep_lane1.log
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_0_0.xconf" "output_sw_0_0_s0" 0 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_0_0.xconf" "output_sw_0_0_s1" 1 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_0_1.xconf" "output_sw_0_1_s0" 0 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_0_1.xconf" "output_sw_0_1_s1" 1 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_0_2.xconf" "output_sw_0_2_s0" 0 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_0_2.xconf" "output_sw_0_2_s1" 1 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_0_4.xconf" "output_sw_0_4_s0" 0 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_0_4.xconf" "output_sw_0_4_s1" 1 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_10_0.xconf" "output_sw_10_0_s0" 0 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_10_0.xconf" "output_sw_10_0_s1" 1 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_10_1.xconf" "output_sw_10_1_s0" 0 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_10_1.xconf" "output_sw_10_1_s1" 1 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_10_2.xconf" "output_sw_10_2_s0" 0 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_10_2.xconf" "output_sw_10_2_s1" 1 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_10_4.xconf" "output_sw_10_4_s0" 0 >> run_sweep_lane1.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_10_4.xconf" "output_sw_10_4_s1" 1 >> run_sweep_lane1.log 2>&1
echo ALL DONE >> run_sweep_lane1.log
