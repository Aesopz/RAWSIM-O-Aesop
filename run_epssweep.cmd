@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%E in (05 10 20) do (
  echo EPS%%E-500 START >> run_epssweep.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\pvgs_eps%%E.xconf" "output_eps%%E_500" 0 >> run_epssweep_runs.log 2>&1
  echo EPS%%E-500 done >> run_epssweep.log
)
echo EPSSWEEP-DONE >> run_epssweep.log
