@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\fixed_fill1350_inv70.xsett Material\Instances\CoreBenchmark\small\m4g.xconf out\m4g_incr 0 > out\m4g_incr_stdout.log 2> out\m4g_incr_stderr.log
echo DONE > out\m4g_incr_done.flag
