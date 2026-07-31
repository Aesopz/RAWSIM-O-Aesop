@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo DRIFT-pvgs_e_eps START >> run_pvgs_tier_port.log
"%CLI%" "%SM%\small.xlayo" "%SM%\fixed_1150.xsett" "%SM%\pvgs_e_eps.xconf" "output_drift_pvgseeps" 0 >> run_pvgs_tier_port_runs.log 2>&1
if errorlevel 1 (echo DRIFT FAIL >> run_pvgs_tier_port.log) else (echo DRIFT SUCCESS >> run_pvgs_tier_port.log)
echo TIERPORT START >> run_pvgs_tier_port.log
"%CLI%" "%SM%\small.xlayo" "%SM%\fixed_1150.xsett" "%SM%\pvgs_e_eps_tier.xconf" "output_pvgseeps_tier" 0 >> run_pvgs_tier_port_runs.log 2>&1
if errorlevel 1 (echo TIERPORT FAIL >> run_pvgs_tier_port.log) else (echo TIERPORT SUCCESS >> run_pvgs_tier_port.log)
echo PVGS-TIERPORT-DONE >> run_pvgs_tier_port.log
