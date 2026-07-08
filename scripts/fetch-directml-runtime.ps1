#Requires -Version 5.1
<#
.SYNOPSIS
  Prepare DirectML native runtime for Echo (Sherpa-ONNX 1.13.4).

.DESCRIPTION
  Copies Sherpa-ONNX DLLs built with -DSHERPA_ONNX_ENABLE_DIRECTML=ON into
  native/win-x64/directml/. Echo.App MSBuild then deploys them on build.

  Official org.k2fsa.sherpa.onnx NuGet is CPU-only. You must supply a DirectML build.

.PARAMETER SourceDir
  Folder containing sherpa-onnx-c-api.dll and onnxruntime.dll from a DirectML build.

.PARAMETER Build
  Clone and build sherpa-onnx v1.13.4 with DirectML (requires CMake, VS 2022, Windows 10 SDK).

.EXAMPLE
  .\scripts\fetch-directml-runtime.ps1 -SourceDir D:\build\sherpa-onnx\build\bin\Release
#>
param(
    [string]$SourceDir = "",
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$SherpaVersion = "1.13.4"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$TargetDir = Join-Path $RepoRoot "native\win-x64\directml"
$RequiredFiles = @(
    "sherpa-onnx-c-api.dll",
    "onnxruntime.dll",
    "DirectML.dll"
)

function Resolve-CMake {
    if (Get-Command cmake -ErrorAction SilentlyContinue) {
        return (Get-Command cmake).Source
    }

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsRoot = & $vswhere -latest -property installationPath
        if ($vsRoot) {
            $cmake = Join-Path $vsRoot "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
            if (Test-Path $cmake) {
                return $cmake
            }
        }
    }

    throw "CMake not found. Install Visual Studio with C++ CMake tools or add cmake to PATH."
}

function Test-DirectMlRuntime([string]$Dir) {
    foreach ($file in $RequiredFiles) {
        $path = Join-Path $Dir $file
        if (-not (Test-Path $path)) {
            throw "Missing $file in $Dir"
        }
    }
}

function Copy-Runtime([string]$From) {
    Test-DirectMlRuntime -Dir $From
    New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
    Get-ChildItem -Path $From -Filter "*.dll" | Copy-Item -Destination $TargetDir -Force
    Write-Host "Copied DirectML runtime from $From to $TargetDir"
    Write-Host "Rebuild Echo.App to enable GPU (DirectML) in settings."
}

function Build-SherpaDirectMl {
    $cmake = Resolve-CMake
    Write-Host "Using CMake: $cmake"

    $work = Join-Path $env:TEMP "echo-sherpa-onnx-$SherpaVersion"
    if (-not (Test-Path $work)) {
        git clone --depth 1 --branch "v$SherpaVersion" https://github.com/k2-fsa/sherpa-onnx.git $work
    }

    $buildDir = Join-Path $work "build-directml"
    New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
    Push-Location $buildDir
    try {
        & $cmake -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON -DSHERPA_ONNX_ENABLE_DIRECTML=ON ..
        if ($LASTEXITCODE -ne 0) { throw "CMake configure failed with exit code $LASTEXITCODE" }
        & $cmake --build . --config Release --target sherpa-onnx-c-api
        if ($LASTEXITCODE -ne 0) { throw "CMake build failed with exit code $LASTEXITCODE" }
    }
    finally {
        Pop-Location
    }

    $candidateDirs = @(
        (Join-Path $buildDir "lib\Release"),
        (Join-Path $buildDir "bin\Release"),
        (Join-Path $buildDir "Release")
    )

    foreach ($binDir in $candidateDirs) {
        if (Test-Path (Join-Path $binDir "sherpa-onnx-c-api.dll")) {
            Copy-Runtime -From $binDir
            return
        }
    }

    throw "Build finished but sherpa-onnx-c-api.dll not found under $buildDir"
}

if ($Build) {
    Build-SherpaDirectMl
    exit 0
}

if ($SourceDir -ne "") {
    Copy-Runtime -From (Resolve-Path $SourceDir)
    exit 0
}

Write-Host @"
DirectML runtime for Echo (Sherpa-ONNX $SherpaVersion)

Option A — copy from an existing DirectML build:
  .\scripts\fetch-directml-runtime.ps1 -SourceDir <path-to-Release-folder>

Option B — build from source (CMake + VS 2022 + Windows 10 SDK):
  .\scripts\fetch-directml-runtime.ps1 -Build

After DLLs are in native\win-x64\directml\, run:
  dotnet build src/Echo.App

See docs/gpu-directml.md for verification on AMD Radeon.
"@
