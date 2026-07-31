Set-Location "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"
$o100="$sm\small_o100_mu100_4h_inv70.xsett"
Remove-Item run_pvgs_eps.log -Force -ErrorAction SilentlyContinue
foreach($tag in "0p5","1p0","2p0"){
  foreach($s in 0,1){
    & $cli "$sm\small.xlayo" $o100 "$sm\pvgs_tier_e$tag.xconf" "output_pvgs_e${tag}_s$s" $s *>> run_pvgs_eps_runs.log
    "e$tag-s$s $LASTEXITCODE"|Out-File -Append -Encoding utf8 run_pvgs_eps.log
  }
}
"ALLDONE"|Out-File -Append -Encoding utf8 run_pvgs_eps.log
