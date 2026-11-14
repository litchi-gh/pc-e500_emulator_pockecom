$ErrorActionPreference = 'Stop'

# Runs the program hidden and waits for it, but not for ever: a dialog it
# should not show (an error) would wait unseen, and the build with it.
# 隠したまま動かして終わりを待つ（見えないダイアログで止まったら、時間で打ち切る）。
function Invoke-Hidden([string[]]$Arguments, [int]$Seconds) {
    $process = Start-Process -FilePath (Join-Path $PSScriptRoot 'app/PC-E500.exe') -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($Seconds * 1000)) { $process.Kill(); throw "PC-E500.exe $($Arguments[0]) did not finish within $Seconds s (a dialog waiting unseen?)." }
    return $process.ExitCode
}
Push-Location $PSScriptRoot
try {
    & cmd /d /c tools\test-native.cmd
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
    # The PC-E500 ROM Ver.7.2, named model(version).ROM, or the older PC-E500.rom.
    $romPath = @('PC-E500(7.2).ROM', 'PC-E500.rom') | ForEach-Object { Join-Path $PSScriptRoot ('app/rom/' + $_) } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $romPath) { throw 'app/rom has no PC-E500(7.2).ROM (or PC-E500.rom).' }
    $testOutput = Join-Path $PSScriptRoot 'tests/output'
    New-Item -ItemType Directory -Force $testOutput | Out-Null
    $code = Invoke-Hidden @('--smoke-test', ('"' + $romPath + '"'), ('"' + $testOutput + '"')) 900
    Get-Content -LiteralPath (Join-Path $testOutput 'smoke-result.txt')
    if ($code -ne 0) { throw 'Desktop integration tests failed.' }
    # The Python launcher where there is one: a bare python may lack Pillow.
    $python = if (Get-Command py -ErrorAction SilentlyContinue) { 'py' } else { 'python' }
    & $python tests/test_hardware.py
    if ($LASTEXITCODE -ne 0) { throw 'ROM hardware integration tests failed.' }
    # Every ROM image in app/rom through the same steps (screens in tests/output/roms).
    & $python tests/test_roms.py
    if ($LASTEXITCODE -ne 0) { throw 'Per-ROM tests failed.' }
} finally { Pop-Location }
