@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo W2P20 seed0 >> run_w2p.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_w2p20.xconf" "output_ic_w2p20_s0" 0 >> run_w2p_runs.log 2>&1
echo W2P-DONE >> run_w2p.log
