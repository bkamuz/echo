# Qualcomm NPU (QNN / HTP) on Windows ARM64 — spike notes

Echo targets **Snapdragon Windows** (Copilot+ PCs, Snapdragon X Elite / X Plus) for optional NPU acceleration of **GigaAM** and **Omnilingual** via Sherpa-ONNX. This document captures research, architecture, and what is **not** working yet.

Whisper stays CPU-only (same as DirectML).

## Hardware target

| Platform | SoC examples | NPU backend |
|----------|--------------|---------------|
| Windows on ARM64 (Snapdragon) | X Elite (SC8380XP), X Plus (SC8380), X2 | Qualcomm Hexagon **HTP** via **QNN** |

DirectML is **x64-only** in Echo today and is excluded from `win-arm64` release zips. NPU is the complementary acceleration path for ARM64 laptops.

## Two different “QNN” stories (important)

### A) Sherpa-ONNX Android QNN path (binary models)

Upstream docs ([Qualcomm NPU (QNN, HTP)](https://k2-fsa.github.io/sherpa/onnx/qnn/index.html)) describe:

- Build with `SHERPA_ONNX_ENABLE_QNN=ON` using **Android NDK** (`build-android-arm64-v8a.sh`).
- Run **QNN-converted binary models** (`model.bin` / `libmodel.so`), not stock ONNX GigaAM/Omnilingual.
- Ship QNN SDK libs (`QnnHtp.so`, etc.) and set `ADSP_LIBRARY_PATH`.

This path is **Android-first**. It does **not** map 1:1 to Echo’s existing ONNX model bundles.

As of sherpa-onnx **master** (checked 2026-09):

- `Provider` enum has **no** `kQNN` — only CPU, CUDA, CoreML, DirectML, OpenVINO, etc.
- `StringToProvider("qnn")` falls through to **CPU**.
- QNN integration lives in a **separate code path** (`qnn/qnn-model.cc`) for precompiled QNN graphs.

**Conclusion:** stock Sherpa NuGet + `provider=qnn` will **not** accelerate Echo models today.

### B) ONNX Runtime QNN Execution Provider (Windows ARM64)

Microsoft / Qualcomm document **ORT QNN EP** for Windows on Snapdragon:

- [ONNX Runtime QNN EP](https://onnxruntime.ai/docs/execution-providers/QNN-ExecutionProvider.html)
- [Windows on Snapdragon — ORT QNN plugin](https://docs.qualcomm.com/nav/home/ort-qnn-ep-plugin.html)
- Standalone plugin package: [`onnxruntime-qnn`](https://github.com/onnxruntime/onnxruntime-qnn) (2.x plugin model)

Requirements for inference on-device:

- **Windows ARM64** device with Qualcomm NPU
- **Quantized** ONNX models (QDQ / `.qdq.onnx`) — float32 GigaAM/Omnilingual ONNX **as shipped** are unlikely to run on HTP without conversion
- QNN runtime DLLs (`QnnHtp.dll`, `onnxruntime_providers_qnn.dll`, …)
- Sherpa-ONNX built against an ORT that exposes `OrtSessionOptionsAppendExecutionProvider_QNN`

**Conclusion:** the plausible Echo path is **ORT QNN EP inside a custom Sherpa build**, not the Android binary-model workflow — but model quantization/conversion is still an open work item.

## Can Echo’s current models run on HTP as-is?

| Model | Format today | HTP-ready? |
|-------|--------------|------------|
| GigaAM v3 (`e2e`, `e2e-ctc`, `rnnt`) | Float ONNX + tokens | **No** — needs QDQ quantization + op coverage validation |
| GigaAM Multilingual CTC | Float ONNX + tokens | **No** — same |
| Omnilingual 300M | Float ONNX | **No** — same |

Expect a pipeline similar to ORT docs: quantize on **win-x64** (or Linux x64), validate on **win-arm64** with QNN EP, then ship optional `-qnn` model variants (similar to how DirectML sometimes needs CPU fallback for unsupported ops).

## Echo scaffolding (this spike)

### Config / provider mapping

| UI label | `config.json` `device` | Sherpa provider string |
|----------|------------------------|-------------------------|
| CPU | `cpu` | `cpu` |
| GPU (DirectML) | `directml` | `directml` (win-x64 only) |
| NPU (Qualcomm QNN) | `npu` | `qnn` |

Legacy alias: `"qnn"` in config normalizes to `npu`.

### Platform probe

- `INpuAvailability` — cross-platform interface
- `WindowsNpuAvailability` — win-arm64 detection + heuristics:
  - `QnnHtp.dll` in System32 or Echo `qnn/` folder
  - Registry CPU vendor / product strings containing Qualcomm / Snapdragon
- Settings shows **NPU** on win-arm64; disabled with tooltip when hardware not detected

### Runtime packaging (mirrors DirectML)

| DirectML (today) | QNN (planned) |
|------------------|---------------|
| `%APPDATA%\Echo\directml\` | `%APPDATA%\Echo\qnn\` |
| GitHub tag `directml-runtime-<sherpa>` | GitHub tag `qnn-runtime-<sherpa>` ( **not published yet** ) |
| Verify `OrtSessionOptionsAppendExecutionProvider_DML` | Verify `OrtSessionOptionsAppendExecutionProvider_QNN` |
| `scripts/fetch-directml-runtime.ps1` | `scripts/fetch-qnn-runtime.ps1` (placeholder) |
| `.github/directml-sherpa-version` | `.github/qnn-sherpa-version` |

Default Release / portable zip remains **CPU-only**. QNN natives are downloaded on first NPU selection (when release exists).

### Fallback

`SherpaOfflineEngine` logs a warning and **retries on CPU** when `qnn` load fails (same as DirectML).

## What works / what does not (honest status)

| Item | Status |
|------|--------|
| Settings UI shows NPU on Snapdragon win-arm64 | **Scaffolded** |
| Hardware probe | **Heuristic** (registry + `QnnHtp.dll`) |
| Config persistence (`device: npu`) | **Works** |
| CPU / DirectML paths | **Unchanged** |
| QNN runtime download | **Blocked** — no `qnn-runtime-*` GitHub release yet |
| Sherpa NuGet 1.13.4 QNN provider | **Not present** — CPU-only ORT |
| End-to-end NPU transcription | **Not working** |
| GigaAM/Omnilingual QNN model artifacts | **Not created** |

## Blockers for end-to-end

1. **Sherpa-ONNX build** — need `win-arm64` shared libs with QNN-enabled ORT (no official prebuilt in k2-fsa releases today; win-arm64 tarballs are CPU-only).
2. **Redistributable QNN DLLs** — Qualcomm SDK licensing / bundling; ORT QNN plugin packages exist for Python/NuGet but must be reconciled with Sherpa’s linked ORT.
3. **Model conversion** — float ONNX → QDQ ONNX (or Android-style QNN binary) per engine variant.
4. **Validation hardware** — CI lacks Snapdragon runners; manual Copilot+ PC required.
5. **PR #3** — sherpa 1.13.8 bump is still draft; QNN work may rebase on that tag later.

## Phased plan

### Phase 0 — Spike (this PR)

- [x] Research doc
- [x] `ExecutionProvider.Npu` + config `npu` → sherpa `qnn`
- [x] `INpuAvailability` + Windows probe
- [x] Settings UI + i18n + CPU fallback
- [x] Runtime installer skeleton + version pin file
- [ ] Published `qnn-runtime-*` assets

### Phase 1 — Runtime

- Build Sherpa-ONNX `win-arm64` with QNN ORT (watch upstream CMake flags; may require `SHERPA_ONNX_ENABLE_QNN=ON` + Windows SDK).
- Publish maintainer release `qnn-runtime-1.13.x` with verified DLL set.
- Seed CI cache (mirror DirectML seed workflow).

### Phase 2 — Models

- Pick one model (likely GigaAM `e2e-ctc` or Omnilingual) for QDQ conversion.
- Document size/latency/accuracy tradeoffs vs CPU on Snapdragon X Elite.
- Optional separate download (`*-qnn` model tag) to avoid bloating default ONNX.

### Phase 3 — Productize

- Enable NPU by default when probe + runtime + model all succeed.
- Telemetry via `echo.log` (`provider=qnn`, timings).
- README / release notes for Copilot+ users.

## Developer: prepare QNN natives (placeholder)

```powershell
# When a QNN-enabled Sherpa build exists, copy DLLs into:
.\scripts\fetch-qnn-runtime.ps1 -SourceDir D:\path\to\sherpa-qnn-arm64\bin

# Or attempt experimental build (requires QNN SDK + VS 2022 on win-arm64):
.\scripts\fetch-qnn-runtime.ps1 -Build
```

Required files in `native/win-arm64/qnn/` (minimum):

- `sherpa-onnx-c-api.dll`
- `onnxruntime.dll` (must export `OrtSessionOptionsAppendExecutionProvider_QNN`)
- `onnxruntime_providers_qnn.dll`
- `QnnHtp.dll`, `QnnSystem.dll` (+ dependent QNN libs from the same SDK drop)

Do **not** mix CPU and QNN Sherpa binaries.

## References

- [Echo DirectML doc](./gpu-directml.md)
- [sherpa-onnx QNN index](https://k2-fsa.github.io/sherpa/onnx/qnn/index.html)
- [sherpa-onnx win-arm64 prebuilts](https://k2-fsa.github.io/sherpa/onnx/install/windows/generated/download/windows_arm64.html) (CPU-only)
- [ONNX Runtime QNN EP](https://onnxruntime.ai/docs/execution-providers/QNN-ExecutionProvider.html)
- [onnxruntime-qnn plugin repo](https://github.com/onnxruntime/onnxruntime-qnn)
- [Qualcomm Windows on Snapdragon ORT guide](https://docs.qualcomm.com/doc/80-62010-1/topic/ort.html)
