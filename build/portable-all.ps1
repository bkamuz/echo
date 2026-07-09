param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$portableScript = Join-Path $PSScriptRoot "portable.ps1"
$releaseDir = Join-Path $root "dist\releases"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (dotnet msbuild "$root\src\Echo.App\Echo.App.csproj" -nologo -getProperty:Version).Trim()
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

& $portableScript -Runtime win-x64 -Version $Version
if ($LASTEXITCODE) { throw "portable.ps1 win-x64 failed (exit $LASTEXITCODE)" }

function New-UnixPortable {
    param(
        [string]$Runtime
    )

    & $publishScript -Runtime $Runtime -Version $Version
    if ($LASTEXITCODE) { throw "publish.ps1 $Runtime failed (exit $LASTEXITCODE)" }

    $sourceDir = Join-Path $root "dist\$Runtime"
    $mainExe = Join-Path $sourceDir "Echo.App"
    if (-not (Test-Path $mainExe)) {
        throw "Publish output missing: $mainExe"
    }

    $archiveName = "Echo-$Version-$Runtime-portable.tar.gz"
    $archivePath = Join-Path $releaseDir $archiveName
    if (Test-Path $archivePath) {
        Remove-Item $archivePath -Force
    }

    tar -czf $archivePath -C $sourceDir .
    $sizeMb = [math]::Round((Get-Item $archivePath).Length / 1MB, 1)
    Write-Host "Portable: $archivePath ($sizeMb MB)"
}

New-UnixPortable -Runtime linux-x64
New-UnixPortable -Runtime osx-arm64

Write-Host "All portable archives in $releaseDir"
