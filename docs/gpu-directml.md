# DirectML on Windows (AMD Radeon)

Echo can accelerate **GigaAM** and **Omnilingual** using Sherpa-ONNX with the `directml` execution provider. This uses your GPU (AMD Radeon iGPU/dGPU, Intel, or NVIDIA) via DirectML — no CUDA Toolkit required.

Whisper always runs on CPU in this release.

## Quick check

1. Run Echo from a release zip or installer (includes `directml/` folder).
2. In **Settings → Распознавание → Устройство**, you should see **GPU (DirectML)**.
3. Select GigaAM, choose **GPU (DirectML)**, dictate a 10+ second phrase.
4. Check logs for `Loading GigaAM ... (provider=directml)` and compare `transcribe=...ms` with CPU.

If **GPU (DirectML)** is not shown, the `directml/` folder is missing next to `Echo.App.exe`.

## Obtain DirectML DLLs (developers)

The `org.k2fsa.sherpa.onnx` NuGet package (1.13.4) ships **CPU-only** native libraries. You need a matching **1.13.4** build with DirectML enabled.

### Option A — copy from your own build

```powershell
.\scripts\fetch-directml-runtime.ps1 -SourceDir D:\path\to\sherpa\Release
```

### Option B — build Sherpa from source

Requirements: Git, **Visual Studio 2022+** with C++ and CMake (cmake is auto-detected from VS).

```powershell
.\scripts\fetch-directml-runtime.ps1 -Build
```

This clones `k2-fsa/sherpa-onnx` tag `v1.13.4`, configures `-DSHERPA_ONNX_ENABLE_DIRECTML=ON`, and copies Release DLLs. First build takes ~5–10 minutes.

### Required files in `native/win-x64/directml/`

At minimum:

- `sherpa-onnx-c-api.dll`
- `onnxruntime.dll` (must export `OrtSessionOptionsAppendExecutionProvider_DML`)
- `DirectML.dll`

Copy **all** `*.dll` from the same build output — do not mix CPU and DirectML binaries.

## How deployment works

On build/publish (Windows only), if `native/win-x64/directml/sherpa-onnx-c-api.dll` exists:

1. CPU Sherpa/ONNX DLLs from NuGet are bundled inside `Echo.App.exe` (single-file).
2. DirectML DLLs are copied to `directml/` next to `Echo.App.exe` (never mixed into the root).
3. `WindowsDirectMlAvailability` probes `directml/onnxruntime.dll` for the DML export.

Portable zip and Inno Setup include `directml/` when present.

### GitHub Releases (CI)

Release workflow runs on self-hosted runner **`aeza-personal`** (Linux VPS) and builds all platforms from there.

DirectML DLLs are **not built on the server**. The release job:

1. Restores `native/win-x64/directml/` from Actions cache (key from `.github/directml-sherpa-version`).
2. Fails fast on cache miss with a hint to run **Seed DirectML cache**.
3. Publishes zip with `Echo.App.exe` + `directml/`.

#### Seed cache (first time or after version bump)

1. Build DLLs locally: `.\scripts\fetch-directml-runtime.ps1 -Build` (or `-SourceDir`).
2. Create maintainer release with the three DLLs:

```powershell
$sherpa = (Get-Content .github/directml-sherpa-version -Raw).Trim()
gh release create "directml-runtime-$sherpa" `
  --repo bkamuz/echo `
  --title "DirectML runtime $sherpa (maintainer)" `
  --notes "Internal assets for Actions cache seeding. Not for end users." `
  native/win-x64/directml/sherpa-onnx-c-api.dll `
  native/win-x64/directml/onnxruntime.dll `
  native/win-x64/directml/DirectML.dll
```

3. Actions → **Seed DirectML cache** → Run workflow.

#### Rebuild Sherpa on GitHub (rare)

When bumping Sherpa version, update `.github/directml-sherpa-version`, then run **Cache DirectML runtime** on `windows-latest` (requires CMake/VS on hosted runner). Re-upload maintainer release and re-seed if needed.

DLLs are **not** stored in git (see `.gitignore`).

## Fallback behaviour

- If DirectML load fails at runtime, GigaAM/Omnilingual log a warning and **retry on CPU**.
- Legacy `device: "cuda"` in `config.json` is normalized to `cpu`.
- Whisper hides the device selector (CPU only).

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| No **GPU (DirectML)** in settings | `directml/` folder missing next to exe after build/publish |
| **Ordinal Not Found** in `sherpa-onnx-c-api.dll` | Mixed CPU/DirectML natives in the same folder — keep CPU in root, DirectML in `directml/` |
| DirectML option shown but slow/errors | Wrong onnxruntime.dll (CPU build) |
| Load fails, falls back to CPU | Model ops not supported on DirectML; use CPU for that engine |
| `OrtSessionOptionsAppendExecutionProvider_DML` missing | onnxruntime.dll is not the DirectML build |

## References

- [Sherpa-ONNX DirectML issue discussion](https://github.com/k2-fsa/sherpa-onnx/issues/2171)
- [ONNX Runtime DirectML EP](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html)
