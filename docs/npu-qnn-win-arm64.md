# Qualcomm NPU (QNN / HTP) on Windows ARM64 — testable PoC

Echo ships an **experimental Parakeet NPU engine** that runs the encoder on Snapdragon **Hexagon HTP** via **ONNX Runtime QNN EP** (not Sherpa `provider=qnn`).

Whisper / GigaAM / Omnilingual / DirectML paths are unchanged.

## Who can test

| Requirement | Detail |
|-------------|--------|
| OS | Windows 11 on ARM64 |
| SoC | **Snapdragon X Elite** (Hexagon V73) — Copilot+ PC |
| Not supported yet | Snapdragon X Plus (different Hexagon revision; needs recompiled `.bin`) |
| Languages | Parakeet TDT 0.6B v3 — ~25 languages, **English-first** for this PoC |

## Quick test (in Echo)

1. Install Echo **win-arm64** build from this PR branch.
2. Open **Settings → Recognition**.
3. Select engine **Parakeet NPU (experimental)** (or device **NPU (Qualcomm QNN)** — switches engine automatically).
4. Click **Download** (~650 MB model + ~50 MB QNN runtime on first use). Progress appears in the status bar.
5. Hold dictation hotkey, speak **English** for up to ~8 seconds per window (longer utterances are chunked).
6. Open `%APPDATA%\Echo\echo.log` and confirm lines like:
   - `Loading Parakeet NPU pipeline (provider=qnn/htp, ...)`
   - `found QNN NPU device(s)` / `Parakeet NPU ready — encoder on Hexagon HTP via ORT QNN EP`
   - `Parakeet transcribe complete provider=qnn/htp`

### If NPU fails

- Settings revert to CPU + GigaAM with **Could not prepare QNN runtime**.
- Common causes: X Plus instead of X Elite, missing `libQnnHtpV73Skel.so` / `libqnnhtpv73.cat`, incomplete wheel extract, model dir missing `encoder-model.bin` beside `encoder-model.onnx`.

## Self-test console (no UI)

Publish or run the helper:

```powershell
dotnet publish tools/Echo.NpuSelfTest/Echo.NpuSelfTest.csproj -c Release -r win-arm64
.\tools\Echo.NpuSelfTest\bin\Release\net10.0\win-arm64\publish\Echo.NpuSelfTest.exe C:\path\sample-16k-mono.wav
```

First run downloads runtime + model to the same `%APPDATA%\Echo\` folders as the app.

## What gets downloaded

| Asset | Source | License | Size (approx.) |
|-------|--------|---------|----------------|
| ORT 1.24.4 + QNN plugin 2.1.1 DLLs | PyPI wheels (hash-pinned in manifest) | MIT + Qualcomm notices | ~50 MB |
| Parakeet HTP encoder `.bin` + wrapper ONNX | [trsdn/parakeet-tdt-0.6b-v3-htp-int8-8s](https://huggingface.co/trsdn/parakeet-tdt-0.6b-v3-htp-int8-8s) | CC-BY-4.0 | ~632 MB |
| CPU preprocessor + TDT decoder + vocab | [istupakov/parakeet-tdt-0.6b-v3-onnx](https://huggingface.co/istupakov/parakeet-tdt-0.6b-v3-onnx) | CC-BY-4.0 | ~18 MB |

Install locations:

- Runtime: `%APPDATA%\Echo\qnn\`
- Model: `%APPDATA%\Echo\models\parakeet-npu\`

## Architecture (honest)

```
Mic → ParakeetPipeline
  ├─ nemo128.onnx          (CPU / ORT)
  ├─ encoder-model.onnx    (EPContext wrapper → encoder-model.bin on HTP via QNN EP)
  └─ decoder_joint.int8    (CPU / TDT greedy decode)
```

We **do not** use stock Sherpa NuGet for NPU. The old `device=npu → sherpa qnn` path was scaffolding only.

Implementation references (ideas + public URLs, not copied code):

- [openwritr-windows](https://github.com/trsdn/openwritr-windows) — runtime manifest, ORT C API QNN session, Parakeet pipeline
- [ONNX Runtime QNN EP](https://onnxruntime.ai/docs/execution-providers/QNN-ExecutionProvider.html)
- [Windows on Snapdragon ORT guide](https://docs.qualcomm.com/doc/80-62010-1/topic/ort.html)

## Required QNN DLLs (runtime package)

Must all sit together in `%APPDATA%\Echo\qnn\`:

- `onnxruntime.dll`
- `onnxruntime_providers_qnn.dll`
- `QnnHtp.dll`, `QnnHtpPrepare.dll`, `QnnHtpNetRunExtensions.dll`, `QnnSystem.dll`
- `QnnHtpV73Stub.dll`, `libQnnHtpV73Skel.so`, `libqnnhtpv73.cat` ← **required**; missing skel/cat → `STATUS_STACK_BUFFER_OVERRUN (0xC0000409)`

## Developer setup

```powershell
# Optional: manual runtime copy after pip/wheel extract
.\scripts\fetch-qnn-runtime.ps1 -SourceDir C:\path\to\extracted\dlls

# Manifests (hash pins) live in:
# src/Echo.Engines.ParakeetNpu/Assets/qnn-runtime-manifest.json
# src/Echo.Engines.ParakeetNpu/Assets/parakeet-npu-model-manifest.json
```

## Known limits (this PoC)

- Snapdragon **X Elite only** (V73 binary)
- No Russian-first model on NPU yet (use GigaAM on CPU)
- Max **8 s** effective window per HTP compile (chunked decode for longer dictation)
- First download ~700 MB total
- CI has **no** Snapdragon runner — device validation is manual

## Phased next steps

1. Validate on X Elite hardware; tune chunk merge / status UX
2. Publish maintainer `qnn-runtime-*` release mirror (optional; PyPI wheels work today)
3. Explore GigaAM/Omnilingual QDQ + HTP conversion (separate from this Parakeet PoC)
4. X Plus / V79 context binary when available
