param([switch]$Quick)
Set-Location $PSScriptRoot
function Get-Stamp {
    (Get-ChildItem -Path $PSScriptRoot -Recurse -Include *.tex,*.bib -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty LastWriteTime)
}
Write-Host ""
Write-Host "  監看 .tex / .bib 變動，存檔後自動重編。Ctrl+C 結束。" -ForegroundColor Cyan
Write-Host ""
if ($Quick) { & "$PSScriptRoot\build.ps1" -Quick } else { & "$PSScriptRoot\build.ps1" }
$last = Get-Stamp
while ($true) {
    Start-Sleep -Seconds 2
    $now = Get-Stamp
    if ($now -ne $last) {
        $last = $now
        Write-Host ("  [" + (Get-Date -Format "HH:mm:ss") + "] 偵測到變動，重新編譯...") -ForegroundColor Cyan
        if ($Quick) { & "$PSScriptRoot\build.ps1" -NoOpen -Quick } else { & "$PSScriptRoot\build.ps1" -NoOpen }
    }
}