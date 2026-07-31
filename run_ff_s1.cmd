@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo FF-100-S1 START >> run_ff_s1.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\pvgs_e_eps_ff.xconf" "output_ff_100_s1" 1 >> run_ff_s1_runs.log 2>&1
echo FF-500-S1 START >> run_ff_s1.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\pvgs_e_eps_ff.xconf" "output_ff_500_s1" 1 >> run_ff_s1_runs.log 2>&1
echo FF-S1-DONE >> run_ff_s1.log
