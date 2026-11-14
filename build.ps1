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
    & cmd /d /c tools\build-native.cmd
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
    dotnet publish src/desktop/PcE500.csproj -c Release -r win-x64 --self-contained true -o app -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    # The HTML manual in app/manual embeds pictures of the running program;
    # re-render them so they always match the build.
    # The PC-E500 ROM Ver.7.2, named model(version).ROM, or the older PC-E500.rom.
    $rom = @('PC-E500(7.2).ROM', 'PC-E500.rom') | ForEach-Object { Join-Path $PSScriptRoot ('app/rom/' + $_) } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($rom) {
        foreach ($lang in @('ja', 'en')) {
            $images = Join-Path $PSScriptRoot ('app/manual/' + $(if ($lang -eq 'ja') { 'images' } else { 'images-en' }))
            $code = Invoke-Hidden @('--manual-shots', ('"' + $rom + '"'), ('"' + $images + '"'), $lang) 600
            if ($code -ne 0) { Get-Content -LiteralPath (Join-Path $images 'manual-shots.txt'); throw 'Manual screenshots failed.' }
        }
    } else {
        # Said, not skipped in silence: the manual keeps the pictures of an older build.
        Write-Warning 'No PC-E500 ROM in app/rom: the manual''s pictures were not made again.'
    }
    Write-Host 'Ready: app\PC-E500.exe'
} finally { Pop-Location }
