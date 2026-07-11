@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett
set SCRATCH=C:\Users\Aesop\AppData\Local\Temp\claude\C--Users-Aesop-Desktop-EE-RAWSim-O-PP\aaefd475-0310-4ad0-ae40-4d218c9cdf83\scratchpad
echo START > run_sweep_lane2.log
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_20_0.xconf" "output_sw_20_0_s0" 0 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_20_0.xconf" "output_sw_20_0_s1" 1 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_20_1.xconf" "output_sw_20_1_s0" 0 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_20_1.xconf" "output_sw_20_1_s1" 1 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_20_2.xconf" "output_sw_20_2_s0" 0 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_20_2.xconf" "output_sw_20_2_s1" 1 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_20_4.xconf" "output_sw_20_4_s0" 0 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_20_4.xconf" "output_sw_20_4_s1" 1 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_30_0.xconf" "output_sw_30_0_s0" 0 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_30_0.xconf" "output_sw_30_0_s1" 1 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_30_1.xconf" "output_sw_30_1_s0" 0 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_30_1.xconf" "output_sw_30_1_s1" 1 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_30_2.xconf" "output_sw_30_2_s0" 0 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_30_2.xconf" "output_sw_30_2_s1" 1 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_30_4.xconf" "output_sw_30_4_s0" 0 >> run_sweep_lane2.log 2>&1
"%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_30_4.xconf" "output_sw_30_4_s1" 1 >> run_sweep_lane2.log 2>&1
echo ALL DONE >> run_sweep_lane2.log
