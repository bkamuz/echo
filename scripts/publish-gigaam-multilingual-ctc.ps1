#Requires -Version 5.1
<#
.SYNOPSIS
  Publish exported GigaAM Multilingual CTC artifacts as a GitHub Release.

.EXAMPLE
  pwsh ./scripts/publish-gigaam-multilingual-ctc.ps1
#>
param(
    [string]$ArtifactsDir = "",
    [string]$Tag = "gigaam-multilingual-ctc",
    [string]$Repo = "bkamuz/echo"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $ArtifactsDir) {
    $ArtifactsDir = Join-Path $RepoRoot "artifacts\gigaam-multilingual-ctc"
}

$int8 = Join-Path $ArtifactsDir "gigaam_multilingual_ctc_int8.onnx"
$tokens = Join-Path $ArtifactsDir "gigaam_multilingual_ctc_tokens.txt"
foreach ($f in @($int8, $tokens)) {
    if (-not (Test-Path $f)) { throw "Missing artifact: $f" }
}

$notes = @"
Sherpa-ONNX ready export of GigaAM ``multilingual_ctc`` (220M) for Echo.

Source: https://huggingface.co/ai-sage/GigaAM-Multilingual (revision ``ctc``), MIT.
Exported with sherpa NeMo CTC metadata + dynamic int8 quantization.

Files:
- ``gigaam_multilingual_ctc_int8.onnx``
- ``gigaam_multilingual_ctc_tokens.txt``
"@

$existing = gh release view $Tag --repo $Repo 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Release $Tag exists — uploading/replacing assets"
    gh release upload $Tag $int8 $tokens --repo $Repo --clobber
}
else {
    Write-Host "Creating release $Tag"
    gh release create $Tag $int8 $tokens --repo $Repo --title "GigaAM Multilingual CTC (sherpa-onnx)" --notes $notes
}

Write-Host "Published: https://github.com/$Repo/releases/tag/$Tag"
