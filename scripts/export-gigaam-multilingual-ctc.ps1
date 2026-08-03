#Requires -Version 5.1
<#
.SYNOPSIS
  Export GigaAM multilingual_ctc to sherpa-onnx files (fp32 + int8 + tokens).

.DESCRIPTION
  Creates a local venv, installs GigaAM + deps, runs the Python exporter into
  artifacts/gigaam-multilingual-ctc/.

.EXAMPLE
  pwsh ./scripts/export-gigaam-multilingual-ctc.ps1
#>
param(
    [string]$OutDir = "",
    [switch]$SkipInt8
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$Artifacts = if ($OutDir) { $OutDir } else { Join-Path $RepoRoot "artifacts\gigaam-multilingual-ctc" }
$Venv = Join-Path $RepoRoot "artifacts\gigaam-export-venv"
$DownloadRoot = Join-Path $RepoRoot "artifacts\gigaam-download"
$PyScript = Join-Path $PSScriptRoot "export-gigaam-multilingual-ctc.py"

New-Item -ItemType Directory -Force -Path (Split-Path $Artifacts) | Out-Null
New-Item -ItemType Directory -Force -Path $DownloadRoot | Out-Null

if (-not (Test-Path (Join-Path $Venv "Scripts\python.exe"))) {
    Write-Host "Creating venv at $Venv"
    python -m venv $Venv
}

$Python = Join-Path $Venv "Scripts\python.exe"
& $Python -m pip install --upgrade pip
# Torch CPU wheel first (export does not need CUDA).
& $Python -m pip install "torch==2.10.*" "torchaudio==2.10.*" --index-url https://download.pytorch.org/whl/cpu
& $Python -m pip install "onnx" "onnxruntime==1.22.*" "soundfile" "numpy"
& $Python -m pip install "git+https://github.com/salute-developers/GigaAM.git@main"

$args = @(
    $PyScript,
    "--out-dir", $Artifacts,
    "--download-root", $DownloadRoot
)
if ($SkipInt8) { $args += "--skip-int8" }

Write-Host "Running exporter…"
& $Python @args
if ($LASTEXITCODE -ne 0) {
    throw "Export failed with exit code $LASTEXITCODE"
}

Write-Host "Artifacts in $Artifacts"
Get-ChildItem $Artifacts | Format-Table Name, Length
