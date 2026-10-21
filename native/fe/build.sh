#!/usr/bin/env bash
# Rebuilds the bundled FastEnhancer natives (faster-enhancer.c, MIT) from Linux.
# Requires: cmake, ninja, gcc, llvm-mingw (LLVM_MINGW=/path).
set -euo pipefail
FEC_SHA=6838816e9ea5369f94f64c57d900796aa9e6a2d5
HERE=$(cd "$(dirname "$0")" && pwd)
LLVM_MINGW=${LLVM_MINGW:-/home/box/llvm-mingw}
WORK=$(mktemp -d)
git clone -q https://github.com/kdrkdrkdr/faster-enhancer.c "$WORK/src"
git -C "$WORK/src" checkout -q "$FEC_SHA"
cp "$WORK/src/weights/fe.q8" "$HERE/fe.q8"
cp "$WORK/src/LICENSE" "$HERE/LICENSE.faster-enhancer.txt"

build() { # rid cc system processor out
  local rid=$1 cc=$2 sys=$3 proc=$4 out=$5 b="$WORK/b-$1"
  cmake -S "$WORK/src" -B "$b" -G Ninja -DCMAKE_BUILD_TYPE=Release -DFE_BUILD_TESTS=OFF -DBUILD_TESTING=OFF \
    -DCMAKE_C_COMPILER="$cc" -DCMAKE_SYSTEM_NAME="$sys" -DCMAKE_SYSTEM_PROCESSOR="$proc" -DCMAKE_POSITION_INDEPENDENT_CODE=ON >/dev/null
  cmake --build "$b" --target fe >/dev/null
  mkdir -p "$HERE/$rid"
  "$cc" -O2 -shared -fvisibility=hidden -I"$WORK/src/include" "$HERE/echo_fe_shim.c" "$b/libfe.a" -lm -o "$HERE/$rid/$out" ${6:-}
}
build linux-x64 gcc Linux x86_64 libecho_fe.so
build win-x64 "$LLVM_MINGW/bin/x86_64-w64-mingw32-clang" Windows AMD64 echo_fe.dll "-static -Wl,--strip-all"
build win-arm64 "$LLVM_MINGW/bin/aarch64-w64-mingw32-clang" Windows ARM64 echo_fe.dll "-static -Wl,--strip-all"
strip "$HERE/linux-x64/libecho_fe.so"
rm -rf "$WORK"
ls -l "$HERE"/*/ "$HERE/fe.q8"
