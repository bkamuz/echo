param(
    [string]$Runtime = "win-x64",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root

$mainExe = if ($Runtime.StartsWith("win-", [StringComparison]::Ordinal)) { "Echo.App.exe" } else { "Echo.App" }
$versionArgs = if ([string]::IsNullOrWhiteSpace($Version)) { @() } else { @("-p:Version=$Version") }

Write-Host "Publishing Echo for $Runtime (single-file)..."
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
    @versionArgs `
    -o $outDir
if ($LASTEXITCODE) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Get-ChildItem $outDir -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne $mainExe } |
    Remove-Item -Force

Write-Host "Output: $root/$outDir"
Get-ChildItem $outDir -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring((Resolve-Path $outDir).Path.Length + 1)
    Write-Host ("  {0} ({1:N1} MB)" -f $rel, ($_.Length / 1MB))
}
Pop-Location
