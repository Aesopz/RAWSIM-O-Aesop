@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (0 1 2 3 4) do (
  echo M1G-S%%S START >> run_4o_i2_10b_8h.log
  "%CLI%" "%SM%\small_4o10b.xlayo" "%SM%\small_o100_mu100_8h.xsett" "%SM%\m1g.xconf" "output_4oi2_m1g_s%%S" %%S >> run_4o_i2_10b_8h_runs.log 2>&1
  if errorlevel 1 (echo M1G-S%%S FAIL >> run_4o_i2_10b_8h.log) else (echo M1G-S%%S SUCCESS >> run_4o_i2_10b_8h.log)
  echo IC-S%%S START >> run_4o_i2_10b_8h.log
  "%CLI%" "%SM%\small_4o10b.xlayo" "%SM%\small_o100_mu100_8h.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_4oi2_ic_s%%S" %%S >> run_4o_i2_10b_8h_runs.log 2>&1
  if errorlevel 1 (echo IC-S%%S FAIL >> run_4o_i2_10b_8h.log) else (echo IC-S%%S SUCCESS >> run_4o_i2_10b_8h.log)
)
echo 4OI2-8H-DONE >> run_4o_i2_10b_8h.log
