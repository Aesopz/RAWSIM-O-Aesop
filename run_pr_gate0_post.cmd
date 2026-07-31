@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\sweep\sw_0_0.xconf" "output_pr_gate0_post" 0 > run_pr_gate0_post.log 2>&1
echo GATE0POST-DONE >> run_pr_gate0_post.log
