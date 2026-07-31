Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli = "RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"
$sm  = "Material\Instances\CoreBenchmark\small"
function Run($cfg, $out, $seed, $tag) {
    "$tag START $(Get-Date -Format o)" | Add-Content run_fer_compare.log
    & $cli "$sm\small.xlayo" "$sm\small_o100_mu100_4h_inv50.xsett" "$sm\$cfg" $out $seed *>> run_fer_compare_runs.log
    "$tag DONE exit=$LASTEXITCODE" | Add-Content run_fer_compare.log
}
Run "tier_earlyrelease.xconf"      "output_fer_on_s1"  1 "TIER-ON-S1"
Run "m1g.xconf"                    "output_fer_m1g_s0" 0 "M1G-S0"
Run "m1g.xconf"                    "output_fer_m1g_s1" 1 "M1G-S1"
Run "split_milp_m2eic_tier.xconf"  "output_fer_off_s1" 1 "TIER-OFF-S1"
"ALL-DONE $(Get-Date -Format o)" | Add-Content run_fer_compare.log
