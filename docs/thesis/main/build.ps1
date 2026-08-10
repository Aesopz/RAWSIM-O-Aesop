param([switch]$NoOpen, [switch]$Quick)
$env:PATH = "C:\Users\Aesop\AppData\Local\Programs\MiKTeX\miktex\bin\x64;" + $env:PATH
Set-Location $PSScriptRoot
$name = "my_ntust_thesis"
$pdf  = Join-Path $PSScriptRoot "$name.pdf"

if (Test-Path $pdf) {
    try { $fs = [IO.File]::Open($pdf,'Open','ReadWrite','None'); $fs.Close() }
    catch {
        Write-Host ""
        Write-Host "  [X] PDF 正被其他程式開啟，無法覆寫。" -ForegroundColor Red
        Write-Host "      請關閉閱讀器，或改用 SumatraPDF（不鎖檔，可邊看邊編譯）。" -ForegroundColor Yellow
        Write-Host ""
        exit 1
    }
}

$steps = if ($Quick) { @('xelatex') } else { @('xelatex','bibtex','xelatex','xelatex') }
$log = ""
foreach ($s in $steps) {
    Write-Host "  -> $s" -ForegroundColor DarkGray
    if ($s -eq 'bibtex') { & bibtex $name 2>&1 | Out-Null }
    else { $log = & xelatex -interaction=nonstopmode "$name.tex" 2>&1 | Out-String }
}

$lines = $log -split "`n"
$errs  = $lines | Select-String -Pattern '^! '
$undef = $lines | Select-String -Pattern 'Citation .* undefined|Reference .* undefined'
$over  = ($lines | Select-String -Pattern 'Overfull \\hbox').Count

Write-Host ""
if ($errs) {
    Write-Host "  [X] 編譯錯誤：" -ForegroundColor Red
    $errs | Select-Object -First 8 | ForEach-Object { Write-Host ("      " + $_.Line.Trim()) -ForegroundColor Red }
    Write-Host "      完整訊息見 $name.log" -ForegroundColor DarkGray
} else {
    Write-Host "  [OK] 編譯成功" -ForegroundColor Green
}
if ($undef) {
    Write-Host ("  [!] 未定義的引用/參照 " + $undef.Count + " 處") -ForegroundColor Yellow
}
if ($over -gt 0) {
    Write-Host ("  [!] Overfull hbox " + $over + " 處（文字超出版面）") -ForegroundColor DarkYellow
}

if (Test-Path $pdf) {
    $pg = $lines | Select-String -Pattern 'Output written on .*\((\d+) pages'
    $n  = if ($pg) { $pg[-1].Matches[0].Groups[1].Value } else { "?" }
    $kb = "{0:N0}" -f ((Get-Item $pdf).Length / 1KB)
    Write-Host ("       $name.pdf   $n 頁   $kb KB") -ForegroundColor Cyan
    if (-not $NoOpen) { Start-Process $pdf }
} else {
    Write-Host "  [X] 未產出 PDF" -ForegroundColor Red
}
Write-Host ""