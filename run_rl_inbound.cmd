@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
del qtable_inbound.txt 2>nul
del qtable_inbound.txt.statelog.csv 2>nul
echo RL-TRAIN-S0 START >> run_rl_inbound.log
"%CLI%" "%SM%\small.xlayo" "%SM%\rl_train.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_rltrain_s0" 0 >> run_rl_inbound_runs.log 2>&1
echo RL-TRAIN-S1 START >> run_rl_inbound.log
"%CLI%" "%SM%\small.xlayo" "%SM%\rl_train.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_rltrain_s1" 1 >> run_rl_inbound_runs.log 2>&1
echo RL-EVAL-RL-S2 START >> run_rl_inbound.log
"%CLI%" "%SM%\small.xlayo" "%SM%\rl_eval.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_rleval_rl_s2" 2 >> run_rl_inbound_runs.log 2>&1
echo RL-EVAL-T1-S2 START >> run_rl_inbound.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_rleval_T1_s2" 2 >> run_rl_inbound_runs.log 2>&1
echo RL-EVAL-T2-S2 START >> run_rl_inbound.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\tier_T2.xconf" "output_rleval_T2_s2" 2 >> run_rl_inbound_runs.log 2>&1
echo RL-EVAL-T3-S2 START >> run_rl_inbound.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\tier_T3.xconf" "output_rleval_T3_s2" 2 >> run_rl_inbound_runs.log 2>&1
echo RL-INBOUND-DONE >> run_rl_inbound.log
