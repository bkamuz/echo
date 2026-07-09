param(
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publishScript = Join-Path $PSScriptRoot "publish.ps1"

& $publishScript -Runtime $Runtime -Version $Version
if ($LASTEXITCODE) { throw "publish.ps1 failed (exit $LASTEXITCODE)" }

if ([string]::IsNullOrWhiteSpace($Version))
{
    $Version = (dotnet msbuild "$root\src\Echo.App\Echo.App.csproj" -nologo -getProperty:Version).Trim()
}
$sourceDir = Join-Path $root "dist\$Runtime"
$releaseDir = Join-Path $root "dist\releases"
$zipName = "Echo-$Version-$Runtime-portable.zip"
$zipPath = Join-Path $releaseDir $zipName

if (-not (Test-Path (Join-Path $sourceDir "Echo.App.exe")))
{
    throw "Publish output missing: $sourceDir\Echo.App.exe"
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
if (Test-Path $zipPath)
{
    Remove-Item $zipPath -Force
}

$staging = Join-Path $env:TEMP "echo-portable-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null
try
{
    Copy-Item (Join-Path $sourceDir "Echo.App.exe") $staging
    $directMl = Join-Path $sourceDir "directml"
    if (Test-Path $directMl)
    {
        Copy-Item $directMl (Join-Path $staging "directml") -Recurse
    }

    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -CompressionLevel Optimal
}
finally
{
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Portable: $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB)"
