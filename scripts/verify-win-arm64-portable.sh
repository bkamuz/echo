#!/usr/bin/env bash
# Verify win-arm64 portable Echo.App.exe ships Sherpa ORT natives, not ML.ORT ones.
# Usage: verify-win-arm64-portable.sh /path/to/Echo.App.exe
set -euo pipefail

exe="${1:?Usage: verify-win-arm64-portable.sh /path/to/Echo.App.exe}"
if [[ ! -f "$exe" ]]; then
  echo "Missing file: $exe" >&2
  exit 1
fi

python3 - "$exe" <<'PY'
import hashlib
import sys

exe_path = sys.argv[1]
data = open(exe_path, "rb").read()

SHERPA_ORT_SHA256 = "968d081836cad9af537105f59e34d4997bca2ce6d78ff39d3e47660620e8a642"
SHERPA_API_SHA256 = "affb939df967d69d509aa6ce6b7c69473c1a35bdd5385ab845e0123c017277a3"
ML_ORT_SHA256 = "724d81ac50b11bfaa01ab3ce01b99fb6734a4b762259a8be4395ca662ce99fe6"

SHERPA_ORT_SIZE = 17_635_328
SHERPA_API_SIZE = 4_584_960
ML_ORT_SIZE = 14_215_752


def contains_pe_blob(size: int, expected_sha256: str) -> bool:
    if len(data) < size:
        return False
    i = 0
    while i < len(data) - 1:
        if data[i : i + 2] != b"MZ":
            i += 1
            continue
        if i + size <= len(data):
            digest = hashlib.sha256(data[i : i + size]).hexdigest()
            if digest == expected_sha256:
                return True
        i += 2
    return False


sherpa_ort = contains_pe_blob(SHERPA_ORT_SIZE, SHERPA_ORT_SHA256)
sherpa_api = contains_pe_blob(SHERPA_API_SIZE, SHERPA_API_SHA256)
ml_ort = contains_pe_blob(ML_ORT_SIZE, ML_ORT_SHA256)
qnn_refs = data.count(b"onnxruntime_providers_qnn")
has_sherpa_runtime_ref = b"org.k2fsa.sherpa.onnx.runtime.win-arm64" in data

# Compressed single-file bundles (v1.9.7) keep native DLLs deflated; reject only the
# uncompressed regression where ML.ORT onnxruntime.dll overwrites Sherpa's copy.
sherpa_stack_ok = sherpa_ort or (has_sherpa_runtime_ref and not ml_ort)

print(f"Echo win-arm64 native stack check: {exe_path}")
print(f"  size={len(data)} bytes")
print(f"  sherpa_onnxruntime.dll (968d0818…): {'present' if sherpa_ort else 'compressed or missing'}")
print(f"  sherpa-onnx-c-api.dll (affb939d…): {'present' if sherpa_api else 'compressed or missing'}")
print(f"  ml_onnxruntime.dll (724d81ac…): {'present — BAD' if ml_ort else 'absent — OK'}")
print(f"  sherpa runtime package ref: {has_sherpa_runtime_ref}")
print(f"  onnxruntime_providers_qnn string refs: {qnn_refs}")

if ml_ort:
    print("FAILED: ML.ORT win-arm64 onnxruntime.dll must not ship in Echo.App.", file=sys.stderr)
    sys.exit(1)

if not sherpa_stack_ok:
    print("FAILED: Sherpa win-arm64 runtime must be bundled.", file=sys.stderr)
    sys.exit(1)

if qnn_refs > 0:
    print("WARNING: QNN provider DLL references found in main bundle (Parakeet uses downloaded runtime).")

print("OK")
PY
