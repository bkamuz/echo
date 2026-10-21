# Third-party notices

Echo downloads some components at runtime. They are **not** included in the Echo archives.

## Whisper Turbo (NPU)

| Component | Source | License | Location after download |
|---|---|---|---|
| Whisper-Large-V3-Turbo, precompiled QNN ONNX (X2 Elite / X Elite), Qualcomm AI Hub Models v0.64.0 | `qaihub-public-assets.s3.us-west-2.amazonaws.com` (links from `huggingface.co/qualcomm/Whisper-Large-V3-Turbo/release_assets.json`) | Apache License 2.0 (Qualcomm AI Hub Models); Whisper weights by OpenAI, MIT | `%APPDATA%\Echo\models\whisper-turbo-npu` |
| Whisper vocabulary (`vocab.json`, `added_tokens.json`) | `huggingface.co/openai/whisper-large-v3-turbo` | MIT (OpenAI) | same folder |
| ONNX Runtime 1.30.0 (`onnxruntime.dll`) | PyPI `onnxruntime` wheel | MIT; notices in `ThirdPartyNotices.onnxruntime.txt` | `%APPDATA%\Echo\qnn-whisper` |
| ONNX Runtime QNN EP 2.6.0 with Qualcomm AI Runtime (QAIRT 2.50) libraries | PyPI `onnxruntime-qnn` wheel | MIT for the EP; QNN/QAIRT libraries under the Qualcomm license in `Qualcomm_LICENSE.pdf`; notices in `ThirdPartyNotices.onnxruntime-qnn.txt` | `%APPDATA%\Echo\qnn-whisper` |

The Qualcomm libraries are used only as part of Echo and are not redistributed separately.
The license files above are extracted from the original wheels next to the libraries.

Apache License 2.0: https://www.apache.org/licenses/LICENSE-2.0
MIT (OpenAI Whisper): https://github.com/openai/whisper/blob/main/LICENSE

## Parakeet NPU

ONNX Runtime 1.24.4 and onnxruntime-qnn 2.1.1 are downloaded to `%APPDATA%\Echo\qnn` in the same way.
