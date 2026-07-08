# DirectML native runtime (not committed)

Place Sherpa-ONNX **1.13.4** Windows x64 DLLs built with DirectML here:

- `sherpa-onnx-c-api.dll`
- `onnxruntime.dll` (DirectML-enabled)
- `DirectML.dll`

Then rebuild Echo.App. MSBuild copies `*.dll` into the app output and writes `directml.enabled`.

See [docs/gpu-directml.md](../../../docs/gpu-directml.md) and `scripts/fetch-directml-runtime.ps1`.
