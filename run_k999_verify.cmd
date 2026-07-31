@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo K999-VERIFY seed0 START >> run_k999_verify.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\sweep\pvgs_k999.xconf" "output_k999_verify_s0" 0 >> run_k999_verify_runs.log 2>&1
if errorlevel 1 (echo K999-VERIFY seed0 FAIL >> run_k999_verify.log) else (echo K999-VERIFY seed0 SUCCESS >> run_k999_verify.log)
echo K999-VERIFY-DONE >> run_k999_verify.log
