@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set L=Material\Instances\CoreBenchmark\small\small.xlayo
set FILL=Material\Instances\CoreBenchmark\small\small_o100_mu100_4h_inv70.xsett
set FIXED=Material\Instances\CoreBenchmark\small\fixed_fill1350_inv70.xsett
set D=Material\Instances\CoreBenchmark\small

%CLI% %L% %FILL%  %D%\hgs_m3_epr.xconf            out\fill_hgs_epr 0
%CLI% %L% %FILL%  %D%\hgs_m3_nothrottle_epr.xconf out\fill_hgs_noth_epr 0
%CLI% %L% %FIXED% %D%\hgs_m3_epr.xconf            out\fixed_hgs_epr 0
%CLI% %L% %FIXED% %D%\hgs_m3_nothrottle_epr.xconf out\fixed_hgs_noth_epr 0

echo DONE_HGS_EPR_RUNS > out\hgs_epr_runs_done.flag
