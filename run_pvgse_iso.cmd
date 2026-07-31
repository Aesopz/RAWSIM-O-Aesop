@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo PVGSE-100FILL START >> run_pvgse_iso.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\pvgs_e.xconf" "output_pvgs_e_100fill_s0" 0 >> run_pvgse_iso_runs.log 2>&1
if errorlevel 1 (echo FAIL >> run_pvgse_iso.log) else (echo SUCCESS >> run_pvgse_iso.log)
echo PVGSE-ISO-DONE >> run_pvgse_iso.log
