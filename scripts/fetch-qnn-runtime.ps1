#Requires -Version 5.1
<#
.SYNOPSIS
  Prepare QNN native runtime for Echo on Windows ARM64 (Snapdragon NPU).

.DESCRIPTION
  Copies Sherpa-ONNX + ORT QNN provider DLLs into native/win-arm64/qnn/.
  Echo mirrors the DirectML pattern: CPU-only NuGet by default, optional QNN
  runtime download on first NPU selection.

  STATUS: Experimental — upstream Sherpa win-arm64 prebuilts are CPU-only and
  there is no published qnn-runtime-* GitHub release yet. Use -SourceDir when
  you have a local QNN-enabled build.

.PARAMETER SourceDir
  Folder containing sherpa-onnx-c-api.dll, onnxruntime.dll, and QNN provider libs.

.PARAMETER Build
  Placeholder for a future automated Sherpa+QNN win-arm64 build (not implemented).

.EXAMPLE
  .\scripts\fetch-qnn-runtime.ps1 -SourceDir D:\build\sherpa-qnn-arm64\bin
#>
param(
    [string]$SourceDir = "",
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$SherpaVersion = (Get-Content (Join-Path $PSScriptRoot "..\.github\qnn-sherpa-version") -Raw).Trim()
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$TargetDir = Join-Path $RepoRoot "native\win-arm64\qnn"
$RequiredFiles = @(
    "sherpa-onnx-c-api.dll",
    "onnxruntime.dll",
    "onnxruntime_providers_qnn.dll",
    "QnnHtp.dll",
    "QnnSystem.dll"
)

function Test-QnnRuntime([string]$Dir) {
    foreach ($file in $RequiredFiles) {
        $path = Join-Path $Dir $file
        if (-not (Test-Path $path)) {
            throw "Missing $file in $Dir"
        }
    }
}

function Copy-Runtime([string]$From) {
    Test-QnnRuntime -Dir $From
    New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
    Get-ChildItem -Path $From -Filter "*.dll" | Copy-Item -Destination $TargetDir -Force
    Write-Host "Copied QNN runtime from $From to $TargetDir"
    Write-Host "Rebuild Echo.App (win-arm64) to enable NPU (QNN) in settings."
}

function Build-SherpaQnn {
    throw @"
Automated Sherpa+QNN win-arm64 build is not wired yet.

Blockers:
  - Sherpa CMake QNN path is documented for Android (SHERPA_ONNX_ENABLE_QNN + NDK).
  - win-arm64 prebuilts from k2-fsa/sherpa-onnx releases are CPU-only.
  - You need a QNN-enabled ORT + matching sherpa-onnx-c-api.dll built together.

Next steps:
  1. Install Qualcomm QNN / QAIRT SDK on a win-arm64 dev machine.
  2. Build sherpa-onnx with QNN ORT (watch upstream for Windows support).
  3. Copy all DLLs from one build output via -SourceDir.

See docs/npu-qnn-win-arm64.md
"@
}

if ($Build) {
    Build-SherpaQnn
}

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    Write-Host @"
QNN runtime fetch (Sherpa $SherpaVersion)

No -SourceDir provided. Expected maintainer release (when published):
  https://github.com/bkamuz/echo/releases/tag/qnn-runtime-$SherpaVersion

Local dev: build or obtain QNN-enabled Sherpa DLLs, then:
  .\scripts\fetch-qnn-runtime.ps1 -SourceDir <folder>

See docs/npu-qnn-win-arm64.md
"@
    exit 0
}

Copy-Runtime -From (Resolve-Path $SourceDir)
