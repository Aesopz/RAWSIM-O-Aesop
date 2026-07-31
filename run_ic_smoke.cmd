@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo GATE3-M2EA seed0 >> run_ic_smoke.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2ea.xconf" "output_gate3_m2ea_s0" 0 >> run_ic_smoke_runs.log 2>&1
echo IC-V4 seed0 >> run_ic_smoke.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic.xconf" "output_ic_v4_s0" 0 >> run_ic_smoke_runs.log 2>&1
echo IC-GL seed0 >> run_ic_smoke.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_gl.xconf" "output_ic_gl_s0" 0 >> run_ic_smoke_runs.log 2>&1
echo IC-PK78 seed0 >> run_ic_smoke.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_pk78.xconf" "output_ic_pk78_s0" 0 >> run_ic_smoke_runs.log 2>&1
echo IC-SMOKE-DONE >> run_ic_smoke.log
