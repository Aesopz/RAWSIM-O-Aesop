@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%B in (1 2 3) do (
  echo FF%%B-500 START >> run_ffbudget.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\pvgs_e_eps_ff%%B.xconf" "output_ff%%B_500" 0 >> run_ffbudget_runs.log 2>&1
  echo FF%%B-500 done >> run_ffbudget.log
)
echo FFBUDGET-DONE >> run_ffbudget.log
