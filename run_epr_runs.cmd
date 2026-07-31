@echo off
cd /d "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set L=Material\Instances\CoreBenchmark\small\small.xlayo
set FILL=Material\Instances\CoreBenchmark\small\small_o100_mu100_4h_inv70.xsett
set FIXED=Material\Instances\CoreBenchmark\small\fixed_fill1350_inv70.xsett
set D=Material\Instances\CoreBenchmark\small

echo RUN1_START %date% %time% >> run_epr_runs.log
%CLI% %L% %FILL% %D%\m4g_epr.xconf out\fill_m4g_epr 0 >> run_epr_runs.log 2>&1
echo RUN1_DONE %errorlevel% %date% %time% >> run_epr_runs.log

echo RUN2_START %date% %time% >> run_epr_runs.log
%CLI% %L% %FIXED% %D%\m4g_epr.xconf out\fixed_m4g_epr 0 >> run_epr_runs.log 2>&1
echo RUN2_DONE %errorlevel% %date% %time% >> run_epr_runs.log

echo RUN3_START %date% %time% >> run_epr_runs.log
%CLI% %L% %FILL% %D%\m3g_noepr.xconf out\fill_m3g_noepr 0 >> run_epr_runs.log 2>&1
echo RUN3_DONE %errorlevel% %date% %time% >> run_epr_runs.log

echo ALL_DONE >> run_epr_runs.log
