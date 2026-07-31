@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
"%CLI%" "%SM%\small.xlayo" "scratch_probe.xsett" "%SM%\sweep\pr_r40.xconf" "output_pr_probe_r40_s0" 0 >> run_pr_probe_runs.log 2>&1
echo [done] r40 >> run_pr_probe.log
"%CLI%" "%SM%\small.xlayo" "scratch_probe.xsett" "%SM%\sweep\pr_r10.xconf" "output_pr_probe_r10_s0" 0 >> run_pr_probe_runs.log 2>&1
echo PRPROBE-DONE >> run_pr_probe.log
