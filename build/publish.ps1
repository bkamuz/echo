param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root

Write-Host "Publishing Echo for $Runtime (single-file + directml/)..."
$outDir = "dist/$Runtime"
dotnet publish "src/Echo.App/Echo.App.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $outDir

# Managed + CPU natives are inside Echo.App.exe; these are publish leftovers.
Get-ChildItem $outDir -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'Echo.App.exe' } |
    Remove-Item -Force

Write-Host "Output: $root/$outDir"
Get-ChildItem $outDir -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring((Resolve-Path $outDir).Path.Length + 1)
    Write-Host ("  {0} ({1:N1} MB)" -f $rel, ($_.Length / 1MB))
}
Pop-Location
