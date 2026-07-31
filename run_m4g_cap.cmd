@echo off
cd /d "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\fixed_fill1350_inv70.xsett Material\Instances\CoreBenchmark\small\m4g.xconf out\m4g_cap 0 > out\m4g_cap_run.log 2>&1
echo DONE > out\m4g_cap_done.flag
