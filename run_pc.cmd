@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo ARM-adm >> run_pc.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_adm.xconf" "output_ic_adm_s0" 0 >> run_pc_runs.log 2>&1
echo ARM-close >> run_pc.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_close.xconf" "output_ic_close_s0" 0 >> run_pc_runs.log 2>&1
echo ARM-pc >> run_pc.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_pc.xconf" "output_ic_pc_s0" 0 >> run_pc_runs.log 2>&1
echo PC-BATCH-DONE >> run_pc.log
