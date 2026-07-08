param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root

Write-Host "Publishing Echo for $Runtime..."
dotnet publish "src/Echo.App/Echo.App.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "dist/$Runtime"

Write-Host "Output: $root/dist/$Runtime"
Pop-Location
