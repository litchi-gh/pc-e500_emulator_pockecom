# Builds the release: dist\PC-E500-Emulator-<version>-win-x64.zip and
# dist\PC-E500-Emulator-<version>-win-x64-setup.exe, both from the same files.
# The version is PcE500.csproj's <Version>; app\README.txt must name it too.
# Needs Inno Setup 6 (ISCC.exe), found where winget or its installer puts it,
# or given with -Iscc.
#   powershell -ExecutionPolicy Bypass -File package.ps1 [-SkipBuild] [-Iscc <path>]
param([switch]$SkipBuild, [string]$Iscc = '')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $SkipBuild) {
        & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }
    $version = ([xml](Get-Content -LiteralPath 'src/desktop/PcE500.csproj' -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { throw 'No <Version> in PcE500.csproj.' }
    # The readme in the release says which version it is; a stale one is a mistake.
    $readme = Get-Content -LiteralPath 'app/README.txt' -TotalCount 1 -Encoding UTF8
    if ($readme -notmatch [regex]::Escape(" $version")) { throw "app/README.txt says '$readme', not version $version." }

    # The release's files, gathered once: the program, its licences and the
    # manual, with empty rom, ram and drive-x folders. Never the user's data.
    $dist = Join-Path $PSScriptRoot 'dist'
    $stage = Join-Path $dist 'stage'
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Force $stage | Out-Null
    $files = 'PC-E500.exe', 'e500core.dll', 'README.txt', 'LICENSE', 'THIRD_PARTY_NOTICES.md',
             'DOTNET-LICENSE.txt', 'DOTNET-THIRD-PARTY-NOTICES.txt', 'WINDOWSDESKTOP-LICENSE.txt'
    foreach ($f in $files) { Copy-Item -LiteralPath (Join-Path 'app' $f) -Destination $stage }
    Copy-Item -LiteralPath 'app/manual' -Destination $stage -Recurse
    Get-ChildItem -LiteralPath (Join-Path $stage 'manual') -Recurse -Filter 'manual-shots.txt' | Remove-Item -Force
    foreach ($d in 'rom', 'ram', 'drive-x') { New-Item -ItemType Directory -Force (Join-Path $stage $d) | Out-Null }

    # The zip, its folders kept even when empty.
    $name = "PC-E500-Emulator-$version-win-x64"
    $zip = Join-Path $dist "$name.zip"
    if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        foreach ($item in Get-ChildItem -LiteralPath $stage -Recurse) {
            $entry = $item.FullName.Substring($stage.Length + 1).Replace('\', '/')
            if ($item.PSIsContainer) {
                if (-not (Get-ChildItem -LiteralPath $item.FullName -Force)) { $archive.CreateEntry("$entry/") | Out-Null }
            } else {
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $item.FullName, $entry, 'Optimal') | Out-Null
            }
        }
    } finally { $archive.Dispose() }

    # The installer, from the same folder.
    if (-not $Iscc) {
        $Iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $Iscc) { throw 'Inno Setup 6 (ISCC.exe) not found: winget install JRSoftware.InnoSetup, or pass -Iscc.' }
    & $Iscc /Q "/DAppVersion=$version" "/DAppSource=$stage" (Join-Path $PSScriptRoot 'installer/PockeComE500.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

    Remove-Item -LiteralPath $stage -Recurse -Force
    Get-ChildItem -LiteralPath $dist -Filter "$name*" | ForEach-Object { Write-Host ("Ready: dist\{0} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
} finally { Pop-Location }
