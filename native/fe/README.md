# FastEnhancer natives (voice cleanup)

Prebuilt `echo_fe` shared libraries wrapping [faster-enhancer.c](https://github.com/kdrkdrkdr/faster-enhancer.c)
(FastEnhancer-Medium, W8A8 int8, 48 kHz, MIT) plus its `fe.q8` weights (565 KB).
Rebuild from Linux with `./build.sh` (needs cmake, ninja, gcc, llvm-mingw). No ONNX Runtime involved.
macOS is not built yet; the setting is hidden when the native is missing.
