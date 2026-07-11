@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett
set SCRATCH=C:\Users\Aesop\AppData\Local\Temp\claude\C--Users-Aesop-Desktop-EE-RAWSim-O-PP\aaefd475-0310-4ad0-ae40-4d218c9cdf83\scratchpad

echo [%TIME%] START > run_w5_matrix.log
for %%v in (m1e m2e) do (
  for %%c in (A B C D) do (
    echo [%TIME%] Running %%v_%%c >> run_w5_matrix.log
    "%CLI%" "%LAYO%" "%SETT%" "%SCRATCH%\%%v_%%c.xconf" "output_w5_%%v_%%c" 0 >> run_w5_matrix.log 2>&1
  )
)
echo [%TIME%] ALL DONE >> run_w5_matrix.log
