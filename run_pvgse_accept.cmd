@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set LAYO=Material\Instances\CoreBenchmark\small\small.xlayo
set SETT=Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett
set SM=Material\Instances\CoreBenchmark\small

echo [%TIME%] START > run_pvgse_accept.log
for %%s in (0 1 2 3 4) do (
  echo [%TIME%] m1g seed %%s >> run_pvgse_accept.log
  "%CLI%" "%LAYO%" "%SETT%" "%SM%\m1g.xconf" "output_acc_m1g_s%%s" %%s >> run_pvgse_accept.log 2>&1
  echo [%TIME%] m2ea seed %%s >> run_pvgse_accept.log
  "%CLI%" "%LAYO%" "%SETT%" "%SM%\sweep\sw_0_0.xconf" "output_acc_m2ea_s%%s" %%s >> run_pvgse_accept.log 2>&1
  echo [%TIME%] pvgse seed %%s >> run_pvgse_accept.log
  "%CLI%" "%LAYO%" "%SETT%" "%SM%\pvgs_e.xconf" "output_acc_pvgse_s%%s" %%s >> run_pvgse_accept.log 2>&1
  echo [%TIME%] m2ea_eps seed %%s >> run_pvgse_accept.log
  "%CLI%" "%LAYO%" "%SETT%" "%SM%\sweep\sw_0_0_eps.xconf" "output_acc_m2ea_eps_s%%s" %%s >> run_pvgse_accept.log 2>&1
  echo [%TIME%] pvgse_eps seed %%s >> run_pvgse_accept.log
  "%CLI%" "%LAYO%" "%SETT%" "%SM%\pvgs_e_eps.xconf" "output_acc_pvgse_eps_s%%s" %%s >> run_pvgse_accept.log 2>&1
)
echo [%TIME%] ALL DONE >> run_pvgse_accept.log
