@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo PVGSE-8H seed0 START >> run_8h_pvgs.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_8h.xsett" "%SM%\pvgs_e.xconf" "output_8h_pvgse_s0" 0 >> run_8h_pvgs_runs.log 2>&1
if errorlevel 1 (echo PVGSE-8H seed0 FAIL >> run_8h_pvgs.log) else (echo PVGSE-8H seed0 SUCCESS >> run_8h_pvgs.log)
echo PVGS-8H-DONE >> run_8h_pvgs.log
