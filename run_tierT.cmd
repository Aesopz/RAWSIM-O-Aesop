@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%T in (2 3) do (
  echo TIER-T%%T-100 START >> run_tierT.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\tier_T%%T.xconf" "output_tierT%%T_100" 0 >> run_tierT_runs.log 2>&1
  echo TIER-T%%T-100 done >> run_tierT.log
)
echo TIERT-DONE >> run_tierT.log
