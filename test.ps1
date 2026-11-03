$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & cmd /d /c tools\test-native.cmd
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
    # The PC-E500 ROM Ver.7.2, named model(version).ROM, or the older PC-E500.rom.
    $romPath = @('PC-E500(7.2).ROM', 'PC-E500.rom') | ForEach-Object { Join-Path $PSScriptRoot ('app/rom/' + $_) } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $romPath) { throw 'app/rom has no PC-E500(7.2).ROM (or PC-E500.rom).' }
    $testOutput = Join-Path $PSScriptRoot 'tests/output'
    $process = Start-Process -FilePath (Join-Path $PSScriptRoot 'app/PC-E500.exe') -ArgumentList @('--smoke-test', ('"' + $romPath + '"'), ('"' + $testOutput + '"')) -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath (Join-Path $testOutput 'smoke-result.txt')
    if ($process.ExitCode -ne 0) { throw 'Desktop integration tests failed.' }
    # The Python launcher where there is one: a bare python may lack Pillow.
    $python = if (Get-Command py -ErrorAction SilentlyContinue) { 'py' } else { 'python' }
    & $python tests/test_hardware.py
    if ($LASTEXITCODE -ne 0) { throw 'ROM hardware integration tests failed.' }
    # Every ROM image in app/rom through the same steps (screens in tests/output/roms).
    & $python tests/test_roms.py
    if ($LASTEXITCODE -ne 0) { throw 'Per-ROM tests failed.' }
} finally { Pop-Location }
