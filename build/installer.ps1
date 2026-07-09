param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$iss = Join-Path $PSScriptRoot "Echo.iss"

& $publishScript -Runtime $Runtime

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc)
{
    throw @"
Inno Setup 6 not found. Install it, then re-run:
  winget install --id JRSoftware.InnoSetup
Or: https://jrsoftware.org/isinfo.php
"@
}

$version = (dotnet msbuild "$root\src\Echo.App\Echo.App.csproj" -nologo -getProperty:Version).Trim()
Write-Host "Building Echo-Setup-$version.exe ..."
& $iscc "/DAppVersion=$version" $iss

$setup = Join-Path $root "dist\installer\Echo-Setup-$version.exe"
if (-not (Test-Path $setup))
{
    throw "Installer build failed: $setup not found"
}

Write-Host "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"
