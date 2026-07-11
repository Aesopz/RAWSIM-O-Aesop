@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett
set SCRATCH=C:\Users\Aesop\AppData\Local\Temp\claude\C--Users-Aesop-Desktop-EE-RAWSim-O-PP\aaefd475-0310-4ad0-ae40-4d218c9cdf83\scratchpad
echo START > run_cap_screen.log
for %%k in (1 2) do (
  for %%w in (0 20) do (
    for %%s in (0 1) do (
      "%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\cap_%%k_%%w.xconf" "output_cap_%%k_%%w_s%%s" %%s >> run_cap_screen.log 2>&1
    )
  )
)
echo ALL DONE >> run_cap_screen.log
