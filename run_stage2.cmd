@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett
set SCRATCH=C:\Users\Aesop\AppData\Local\Temp\claude\C--Users-Aesop-Desktop-EE-RAWSim-O-PP\aaefd475-0310-4ad0-ae40-4d218c9cdf83\scratchpad
echo START > run_stage2.log
for %%w in (40 60) do (
  for %%s in (2 3 4) do (
    "%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\sw_%%w_0.xconf" "output_sw_%%w_0_s%%s" %%s >> run_stage2.log 2>&1
  )
)
echo ALL DONE >> run_stage2.log
