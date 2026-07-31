@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo DRIFT-100 START >> run_ff.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\pvgs_e_eps.xconf" "output_ffdrift_100" 0 >> run_ff_runs.log 2>&1
echo DRIFT-100 done >> run_ff.log
echo FF-100 START >> run_ff.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\pvgs_e_eps_ff.xconf" "output_ff_100" 0 >> run_ff_runs.log 2>&1
echo FF-100 done >> run_ff.log
echo FF-500 START >> run_ff.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu500_4h_inv50.xsett" "%SM%\pvgs_e_eps_ff.xconf" "output_ff_500" 0 >> run_ff_runs.log 2>&1
echo FF-500 done >> run_ff.log
echo FF-ALL-DONE >> run_ff.log
