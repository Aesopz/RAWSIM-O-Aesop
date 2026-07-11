@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett
set SCRATCH=C:\Users\Aesop\AppData\Local\Temp\claude\C--Users-Aesop-Desktop-EE-RAWSim-O-PP\aaefd475-0310-4ad0-ae40-4d218c9cdf83\scratchpad
echo START > run_w4_refine.log
for %%v in (m1e m2e) do (
  for %%w in (10 20) do (
    echo Running %%v_w4_%%w >> run_w4_refine.log
    "%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\%%v_w4_%%w.xconf" "output_w4ref_%%v_%%w" 0 >> run_w4_refine.log 2>&1
  )
)
echo ALL DONE >> run_w4_refine.log
