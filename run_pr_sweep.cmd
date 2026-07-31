@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%r in (pr_r10 pr_r20 pr_r40) do (
  for %%s in (0 1) do (
    echo [%TIME%] %%r seed %%s >> run_pr_sweep.log
    "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\sweep\%%r.xconf" "output_%%r_s%%s" %%s >> run_pr_sweep_runs.log 2>&1
  )
)
echo PRSWEEP-DONE >> run_pr_sweep.log
