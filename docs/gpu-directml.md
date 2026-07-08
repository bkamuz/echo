# DirectML on Windows (AMD Radeon)

Echo can accelerate **GigaAM** and **Omnilingual** using Sherpa-ONNX with the `directml` execution provider. This uses your GPU (AMD Radeon iGPU/dGPU, Intel, or NVIDIA) via DirectML — no CUDA Toolkit required.

Whisper always runs on CPU in this release.

## Quick check

1. Place DirectML-enabled Sherpa DLLs in `native/win-x64/directml/` (see below).
2. Build the app: `dotnet build src/Echo.App`
3. Run Echo. In **Settings → Распознавание → Устройство**, you should see **GPU (DirectML)** if the runtime probe succeeded.
4. Select GigaAM, choose **GPU (DirectML)**, dictate a 10+ second phrase.
5. Check logs for `Loading GigaAM ... (provider=directml)` and compare `transcribe=...ms` with CPU.

If **GPU (DirectML)** is not shown, the app is using the stock CPU-only NuGet runtime.

## Obtain DirectML DLLs

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

On build (Windows only), if `native/win-x64/directml/sherpa-onnx-c-api.dll` exists:

1. MSBuild copies every `*.dll` from that folder into the app output (overwriting CPU NuGet copies).
2. A marker file `directml.enabled` is written.
3. At startup, `WindowsDirectMlAvailability` checks the marker, `sherpa-onnx-c-api.dll`, and the DML export in `onnxruntime.dll`.

## Fallback behaviour

- If DirectML load fails at runtime, GigaAM/Omnilingual log a warning and **retry on CPU**.
- Legacy `device: "cuda"` in `config.json` is normalized to `cpu`.
- Whisper hides the device selector (CPU only).

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| No **GPU (DirectML)** in settings | DLLs missing or `directml.enabled` not in output |
| DirectML option shown but slow/errors | Wrong onnxruntime.dll (CPU build) |
| Load fails, falls back to CPU | Model ops not supported on DirectML; use CPU for that engine |
| `OrtSessionOptionsAppendExecutionProvider_DML` missing | onnxruntime.dll is not the DirectML build |

## References

- [Sherpa-ONNX DirectML issue discussion](https://github.com/k2-fsa/sherpa-onnx/issues/2171)
- [ONNX Runtime DirectML EP](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html)
