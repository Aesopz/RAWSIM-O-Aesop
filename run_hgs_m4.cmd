@echo off
setlocal
set R=C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set L=%R%\Material\Instances\CoreBenchmark\small\small.xlayo
set D=%R%\Material\Instances\CoreBenchmark\small
set EXE=%R%\RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe

echo === regress_m4g ===
"%EXE%" "%L%" "%D%\fixed_fill1350_inv70.xsett" "%D%\m4g.xconf" "%R%\out\regress_m4g" 0
echo regress_m4g DONE

echo === fixed_hgs_m4 ===
"%EXE%" "%L%" "%D%\fixed_fill1350_inv70.xsett" "%D%\hgs_m4.xconf" "%R%\out\fixed_hgs_m4" 0
echo fixed_hgs_m4 DONE

echo === fill_hgs_m4 ===
"%EXE%" "%L%" "%D%\small_o100_mu100_4h_inv70.xsett" "%D%\hgs_m4.xconf" "%R%\out\fill_hgs_m4" 0
echo fill_hgs_m4 DONE

echo ALL_RUNS_COMPLETE > "%R%\out\hgs_m4_runs.done"
