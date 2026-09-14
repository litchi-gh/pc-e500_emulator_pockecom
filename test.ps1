$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & cmd /d /c tools\test-native.cmd
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
    $romPath = (Resolve-Path (Join-Path $PSScriptRoot 'app/rom/PC-E500.rom')).Path
    $testOutput = Join-Path $PSScriptRoot 'tests/output'
    $process = Start-Process -FilePath (Join-Path $PSScriptRoot 'app/PC-E500.exe') -ArgumentList @('--smoke-test', ('"' + $romPath + '"'), ('"' + $testOutput + '"')) -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath (Join-Path $testOutput 'smoke-result.txt')
    if ($process.ExitCode -ne 0) { throw 'Desktop integration tests failed.' }
    & python tests/test_hardware.py
    if ($LASTEXITCODE -ne 0) { throw 'ROM hardware integration tests failed.' }
} finally { Pop-Location }
