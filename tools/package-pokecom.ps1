# Builds the Windows command line and MCP server: dist\pokecom-<version>-win-x64.zip.
# Needs bin\e500core.dll (tools\build-native.cmd) and the .NET 10 SDK; package.ps1 runs it too.
#   powershell -ExecutionPolicy Bypass -File tools\package-pokecom.ps1
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $version = ([xml](Get-Content -LiteralPath 'src/cli/PokeCom.Cli.csproj' -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { throw 'No <Version> in PokeCom.Cli.csproj.' }
    $dist = Join-Path (Get-Location) 'dist'
    $name = "pokecom-$version-win-x64"
    $stage = Join-Path $dist "stage-pokecom"
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    $folder = Join-Path $stage $name
    New-Item -ItemType Directory -Force (Join-Path $folder 'rom') | Out-Null
    $out = Join-Path $stage 'publish'
    & dotnet publish src/cli/PokeCom.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=none -o $out --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    foreach ($f in (Join-Path $out 'pokecom.exe'), (Join-Path $out 'e500core.dll'), 'LICENSE', 'docs/THIRD_PARTY_NOTICES.md', 'third_party/dotnet/DOTNET-LICENSE.txt', 'third_party/dotnet/DOTNET-THIRD-PARTY-NOTICES.txt') {
        Copy-Item -LiteralPath $f -Destination $folder
    }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $text = [System.IO.File]::ReadAllText((Join-Path (Get-Location) 'packaging/windows/README.txt'), $utf8).Replace('@VERSION@', $version).Replace("`r`n", "`n").Replace("`n", "`r`n")
    [System.IO.File]::WriteAllText((Join-Path $folder 'README.txt'), $text, (New-Object System.Text.UTF8Encoding($true)))

    $zip = Join-Path $dist "$name.zip"
    if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        $archive.CreateEntry("$name/rom/") | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $folder -File) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "$name/$($file.Name)", 'Optimal') | Out-Null
        }
    } finally { $archive.Dispose() }
    Remove-Item -LiteralPath $stage -Recurse -Force
    Write-Host ("Ready: dist\$name.zip ({0:N1} MB)" -f ((Get-Item -LiteralPath $zip).Length / 1MB))
} finally { Pop-Location }
