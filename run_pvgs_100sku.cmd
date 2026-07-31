@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%C in (pvgs_e_eps pvgs_m2e) do (
  echo %%C-100SKU-FILL START >> run_pvgs_100sku.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\%%C.xconf" "output_%%C_100fill_s0" 0 >> run_pvgs_100sku_runs.log 2>&1
  if errorlevel 1 (echo %%C-100SKU-FILL FAIL >> run_pvgs_100sku.log) else (echo %%C-100SKU-FILL SUCCESS >> run_pvgs_100sku.log)
)
echo PVGS-100SKU-DONE >> run_pvgs_100sku.log
