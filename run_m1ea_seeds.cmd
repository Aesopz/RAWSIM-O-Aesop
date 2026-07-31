@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%s in (1 2 3 4) do (
  echo seed %%s >> run_m1ea_seeds.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m1ea.xconf" "output_aligned_m1ea_s%%s" %%s >> run_m1ea_seeds_runs.log 2>&1
)
echo M1EA-DONE >> run_m1ea_seeds.log
