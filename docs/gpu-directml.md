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

Windows job in [`.github/workflows/release.yml`](../.github/workflows/release.yml):

1. Restores `native/win-x64/directml/` from Actions cache (`directml-sherpa-1.13.4`).
2. On cache miss, runs `fetch-directml-runtime.ps1 -Build` (~5–10 min once).
3. Publishes zip with `Echo.App.exe` + `directml/`.

To **refresh the cache** after bumping Sherpa version:

1. Update cache key in `release.yml` and `cache-directml.yml` (e.g. `directml-sherpa-1.13.5`).
2. Run Actions → **Cache DirectML runtime** → Run workflow.

Or delete the cache entry under GitHub → Settings → Actions → Caches.

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
