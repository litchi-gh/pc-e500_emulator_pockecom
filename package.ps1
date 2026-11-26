# Builds the release: dist\PC-E500-Emulator-<version>-win-x64.zip and
# dist\PC-E500-Emulator-<version>-win-x64-setup.exe, both from the same files,
# and the command line's dist\pokecom-<version>-win-x64.zip.
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
    # The whole version, at the end of the line: ' 0.0.4' alone also matched 0.0.40 or 0.0.4-beta.
    if ($readme -notmatch (' ' + [regex]::Escape($version) + '\s*$')) { throw "app/README.txt says '$readme', not version $version." }
    # The manual's pictures are made by the build; without a ROM it cannot, and a
    # release would carry the pictures of an older build.
    foreach ($images in 'app/manual/images', 'app/manual/images-en') {
        $done = Join-Path $images 'manual-shots.txt'
        if (-not (Test-Path -LiteralPath $done) -or (Get-Content -LiteralPath $done -TotalCount 1) -ne 'OK' -or (Get-Item -LiteralPath $done).LastWriteTime -lt (Get-Item -LiteralPath 'app/PC-E500.exe').LastWriteTime) {
            throw "The manual's pictures in $images were not made by this build (is a PC-E500 ROM in app/rom?)."
        }
    }

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

    # The command line, in a zip of its own.
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tools/package-pokecom.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'The command line was not packaged.' }
    $pokecom = "pokecom-$version-win-x64.zip"
    if (-not (Test-Path -LiteralPath (Join-Path $dist $pokecom))) { throw "No dist\$pokecom (is src/cli another version?)." }

    # No file is signed, so the release notes carry their SHA-256 for users
    # to check a download against. Written in the form sha256sum -c reads, and
    # printed ready to paste into the notes.
    # 署名がないので、リリースの本文に載せる SHA-256 を書き出す（sha256sum -c で読める形）。
    $packages = Get-ChildItem -LiteralPath $dist -File | Where-Object { $_.Name -eq "$name.zip" -or $_.Name -eq "$name-setup.exe" -or $_.Name -eq $pokecom } | Sort-Object Name
    $sums = Join-Path $dist "$name-SHA256.txt"
    $lines = $packages | ForEach-Object { '{0} *{1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
    [System.IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
    $packages | ForEach-Object { Write-Host ("Ready: dist\{0} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
    Write-Host "Ready: dist\$name-SHA256.txt"
    Write-Host ''
    Write-Host 'For the release notes:'
    Write-Host '## SHA-256'
    Write-Host '```'
    $lines | ForEach-Object { Write-Host $_ }
    Write-Host '```'
} finally { Pop-Location }
