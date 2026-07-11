@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett
set SWEEP=Material\Instances\CoreBenchmark\small\sweep

echo [%TIME%] START > run_d3.log
for %%c in (sw_0_01 sw_40_01) do (
  for %%s in (0 1) do (
    echo [%TIME%] Running %%c seed %%s >> run_d3.log
    "%CLI%" "%LAYO%" "%SETT%" "%SWEEP%\%%c.xconf" "output_%%c_s%%s" %%s >> run_d3.log 2>&1
  )
)
echo [%TIME%] ALL DONE >> run_d3.log
