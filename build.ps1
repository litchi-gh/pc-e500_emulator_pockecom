$ErrorActionPreference = 'Stop'
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
            $shots = Start-Process -FilePath (Join-Path $PSScriptRoot 'app/PC-E500.exe') -ArgumentList @('--manual-shots', ('"' + $rom + '"'), ('"' + $images + '"'), $lang) -WindowStyle Hidden -PassThru -Wait
            if ($shots.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $images 'manual-shots.txt'); throw 'Manual screenshots failed.' }
        }
    }
    Write-Host 'Ready: app\PC-E500.exe'
} finally { Pop-Location }
