@echo off
setlocal
cd /d "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set D=Material\Instances\CoreBenchmark\small
set LD=Material\Instances\CoreBenchmark\large

echo [%date% %time%] starting ps_fixed > out\presentsplit_runner.log
%CLI% %D%\small.xlayo %D%\fixed_fill1350_inv70.xsett      %D%\m4g_presentsplit.xconf out\ps_fixed 0 >> out\presentsplit_runner.log 2>&1
echo [%date% %time%] finished ps_fixed, starting ps_fill >> out\presentsplit_runner.log
%CLI% %D%\small.xlayo %D%\small_o100_mu100_4h_inv70.xsett %D%\m4g_presentsplit.xconf out\ps_fill 0 >> out\presentsplit_runner.log 2>&1
echo [%date% %time%] finished ps_fill, starting ps_large >> out\presentsplit_runner.log
%CLI% %LD%\large_test-toobig-45bot.xlayo %LD%\large_test.xsett %D%\m4g_presentsplit.xconf out\ps_large 0 >> out\presentsplit_runner.log 2>&1
echo [%date% %time%] ALL DONE >> out\presentsplit_runner.log
