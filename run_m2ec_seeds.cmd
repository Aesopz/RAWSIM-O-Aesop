@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%s in (1 2 3 4) do (
  echo seed %%s >> run_m2ec_seeds.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\sweep\m2e_C.xconf" "output_w5_m2e_C_s%%s" %%s >> run_m2ec_seeds_runs.log 2>&1
)
echo M2EC-DONE >> run_m2ec_seeds.log
