#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Thunderstore package for InFalsusAdvVN.

.DESCRIPTION
    Produces dist/<Name>-<Version>.zip containing only what Thunderstore needs:

        manifest.json
        icon.png
        README.md
        CHANGELOG.md
        LICENSE
        BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll

    Source code, the project file, git metadata and build intermediates are never copied.

    The plugin assembly is taken from a local build when the BepInEx/interop install exists,
    otherwise from the committed prebuilt/InFalsusAdvVN.dll (CI has no game assemblies to
    compile against, see prebuilt/README.md).

.PARAMETER Version
    Overrides the version from manifest.json (e.g. 1.1.0). A leading "v" is ignored.

.PARAMETER ExpectedTag
    Tag that triggered the build (e.g. v1.0.0). The script fails when it does not match the
    package version, so a wrong tag can never publish a mismatched package.

.PARAMETER DllPath
    Use this assembly instead of building or using prebuilt/.

.PARAMETER OutputDir
    Destination directory for the zip. Default: dist.

.PARAMETER SkipBuild
    Never invoke dotnet, always use prebuilt/ (unless -DllPath is given).

.EXAMPLE
    ./build/package.ps1 -Version 1.1.0
    ./build/package.ps1 -SkipBuild -ExpectedTag v1.0.0
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $ExpectedTag,
    [string] $DllPath,
    [string] $OutputDir = 'dist',
    [switch] $SkipBuild,
    [switch] $KeepStage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageName = 'InFalsusAdvVN'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$interopDir = Join-Path $env:APPDATA 'r2modmanPlus-local\InFalsus\profiles\Default\BepInEx\interop'

function Write-Step([string] $text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Note([string] $text) { Write-Host "    $text" -ForegroundColor DarkGray }
function Fail([string] $text) { throw "package: $text" }

function Normalize-Version([string] $value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    $clean = $value.Trim()
    if ($clean.StartsWith('v') -or $clean.StartsWith('V')) { $clean = $clean.Substring(1) }
    if ($clean -notmatch '^\d+\.\d+\.\d+$') { Fail "version '$value' is not MAJOR.MINOR.PATCH" }
    return $clean
}

function Get-PngSize([string] $path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 24) { Fail "icon.png is too small to be a PNG" }
    if ($bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50 -or $bytes[2] -ne 0x4E -or $bytes[3] -ne 0x47) {
        Fail "icon.png is not a PNG file"
    }
    $width = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 16))
    $height = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 20))
    return @{ Width = $width; Height = $height }
}

Push-Location $root
try {
    # ------------------------------------------------------------------ manifest
    Write-Step 'Reading manifest.json'
    $manifestPath = Join-Path $root 'manifest.json'
    if (-not (Test-Path $manifestPath)) { Fail 'manifest.json is missing' }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    foreach ($field in 'name', 'version_number', 'description', 'website_url', 'dependencies') {
        if (-not $manifest.PSObject.Properties.Name.Contains($field)) { Fail "manifest.json has no '$field'" }
    }
    if ($manifest.name -ne $packageName) { Fail "manifest name '$($manifest.name)' does not match '$packageName'" }
    if (-not $manifest.dependencies -or $manifest.dependencies.Count -eq 0) {
        Fail 'manifest.json must depend on BepInExPack_IL2CPP'
    }

    $manifestVersion = Normalize-Version $manifest.version_number
    Write-Note "manifest: $($manifest.name) $manifestVersion, $($manifest.dependencies.Count) dependency/ies"

    # ------------------------------------------------------------------ version
    $packageVersion = if ($Version) { Normalize-Version $Version } else { $manifestVersion }
    if ($Version -and $packageVersion -ne $manifestVersion) {
        Write-Host "    overriding manifest version $manifestVersion with $packageVersion" -ForegroundColor Yellow
    }

    if ($ExpectedTag) {
        $tagVersion = Normalize-Version $ExpectedTag
        if ($tagVersion -ne $packageVersion) {
            Fail "tag '$ExpectedTag' does not match package version '$packageVersion' (bump manifest.json first)"
        }
        Write-Note "tag $ExpectedTag matches package version"
    }

    # ------------------------------------------------------------------ icon
    Write-Step 'Checking icon.png'
    $iconPath = Join-Path $root 'icon.png'
    if (-not (Test-Path $iconPath)) { Fail 'icon.png is missing' }
    $size = Get-PngSize $iconPath
    if ($size.Width -ne 256 -or $size.Height -ne 256) {
        Fail "icon.png must be 256x256 for Thunderstore (found $($size.Width)x$($size.Height))"
    }
    Write-Note "icon: 256x256"

    # ------------------------------------------------------------------ assembly
    Write-Step 'Locating the plugin assembly'
    $assembly = $null

    if ($DllPath) {
        $assembly = (Resolve-Path $DllPath).Path
        Write-Note "using -DllPath $assembly"
    }
    elseif (-not $SkipBuild -and (Test-Path (Join-Path $interopDir 'Game.dll'))) {
        Write-Note 'game interop assemblies found, building from source'
        & dotnet build (Join-Path $root "$packageName.csproj") -c Release --nologo -v minimal
        if ($LASTEXITCODE -ne 0) { Fail 'dotnet build failed' }
        $assembly = Join-Path $root "bin\Release\$packageName.dll"
    }

    if (-not $assembly) {
        $prebuilt = Join-Path $root "prebuilt\$packageName.dll"
        if (-not (Test-Path $prebuilt)) {
            Fail ("no assembly available: run 'dotnet build -c Release' on a machine with the game " +
                  "installed, or commit $prebuilt")
        }
        $assembly = $prebuilt
        Write-Note 'using the committed prebuilt assembly (CI has no game assemblies to compile against)'
    }

    if (-not (Test-Path $assembly)) { Fail "assembly not found: $assembly" }
    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($assembly).Version
    if ($assemblyVersion -and $assemblyVersion.ToString(3) -ne $packageVersion) {
        Write-Host ("    warning: assembly version $assemblyVersion differs from package version " +
                    "$packageVersion") -ForegroundColor Yellow
    }
    Write-Note "assembly: $([System.IO.Path]::GetFileName($assembly)) ($([Math]::Round((Get-Item $assembly).Length / 1KB, 1)) KiB)"

    # ------------------------------------------------------------------ stage
    Write-Step 'Staging the package layout'
    $stage = Join-Path $root (Join-Path $OutputDir '_stage')
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    $pluginDir = Join-Path (Join-Path $stage 'BepInEx\plugins') $packageName
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null

    $rootFiles = @('manifest.json', 'icon.png', 'README.md', 'CHANGELOG.md', 'LICENSE')
    foreach ($file in $rootFiles) {
        $source = Join-Path $root $file
        if (-not (Test-Path $source)) {
            if ($file -eq 'LICENSE') { Write-Host "    warning: $file missing, skipping" -ForegroundColor Yellow; continue }
            Fail "$file is missing"
        }
        Copy-Item $source (Join-Path $stage $file)
        Write-Note "+ $file"
    }

    Copy-Item $assembly (Join-Path $pluginDir "$packageName.dll")
    Write-Note "+ BepInEx/plugins/$packageName/$packageName.dll"

    # ------------------------------------------------------------------ zip
    Write-Step 'Writing the zip'
    $dist = Join-Path $root $OutputDir
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    $zipPath = Join-Path $dist "$packageName-$packageVersion.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    # Entries are written by hand with '/' separators: System.IO.Compression.ZipFile uses
    # backslashes on Windows PowerShell, which is not a valid zip path separator for
    # Thunderstore's uploader.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Add-Type -AssemblyName System.IO.Compression

    $zipStream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
    try {
        $archiveWriter = New-Object System.IO.Compression.ZipArchive(
            $zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in (Get-ChildItem $stage -Recurse -File)) {
                $relative = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
                $entry = $archiveWriter.CreateEntry(
                    $relative, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $file.LastWriteTime

                $entryStream = $entry.Open()
                try {
                    $sourceStream = [System.IO.File]::OpenRead($file.FullName)
                    try { $sourceStream.CopyTo($entryStream) } finally { $sourceStream.Dispose() }
                }
                finally { $entryStream.Dispose() }
            }
        }
        finally { $archiveWriter.Dispose() }
    }
    finally { $zipStream.Dispose() }

    # ------------------------------------------------------------------ verify
    Write-Step 'Verifying the archive'
    $required = @(
        'manifest.json',
        'icon.png',
        'README.md',
        'CHANGELOG.md',
        "BepInEx/plugins/$packageName/$packageName.dll"
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = $archive.Entries | Where-Object { $_.Name -ne '' } | ForEach-Object { $_.FullName }
        foreach ($name in $required) {
            if ($entries -notcontains $name) { Fail "archive is missing $name" }
        }

        $forbidden = $entries | Where-Object {
            $_ -match '\.(cs|csproj|sln|user|pdb|last)$' -or
            $_ -match '(^|/)(\.git|bin|obj|src)/'
        }
        if ($forbidden) {
            Fail "archive contains release-unneeded files: $($forbidden -join ', ')"
        }

        Write-Note "$($entries.Count) entries, no source/build files"
        $entries | Sort-Object | ForEach-Object { Write-Note "  $_" }
    }
    finally {
        $archive.Dispose()
    }

    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    $sizeKb = [Math]::Round((Get-Item $zipPath).Length / 1KB, 1)

    if (-not $KeepStage) { Remove-Item $stage -Recurse -Force }

    Write-Host ''
    Write-Host "Package : $zipPath" -ForegroundColor Green
    Write-Host "Version : $packageVersion"
    Write-Host "Size    : $sizeKb KiB"
    Write-Host "SHA256  : $hash"
}
finally {
    Pop-Location
}
