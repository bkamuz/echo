#!/usr/bin/env python3
"""Print OrtApi vtable indices from onnxruntime_c_api.h (ORT 1.24.x)."""
from __future__ import annotations

import re
import sys
import urllib.request

ORT_TAG = sys.argv[1] if len(sys.argv) > 1 else "v1.24.4"
URL = (
    f"https://raw.githubusercontent.com/microsoft/onnxruntime/{ORT_TAG}/"
    "include/onnxruntime/core/session/onnxruntime_c_api.h"
)

text = urllib.request.urlopen(URL, timeout=60).read().decode("utf-8")
start = text.find("struct OrtApi {")
end = text.find("} OrtApi;", start)
lines = text[start:end].splitlines()
indices: dict[str, int] = {}
idx = 0
for line in lines:
    if "ORT_API2_STATUS" in line:
        match = re.search(r"ORT_API2_STATUS\((\w+)", line)
        if match:
            indices[match.group(1)] = idx
            idx += 1
    elif "ORT_CLASS_RELEASE" in line:
        match = re.search(r"ORT_CLASS_RELEASE\((\w+)\)", line)
        if match:
            indices["Release" + match.group(1)] = idx
            idx += 1
    elif "(ORT_API_CALL*" in line and "ORT_API2" not in line:
        match = re.search(r"\(ORT_API_CALL\* (\w+)\)", line)
        if match:
            indices[match.group(1)] = idx
            idx += 1

tracked = [
    "GetErrorCode",
    "GetErrorMessage",
    "CreateEnv",
    "CreateSession",
    "Run",
    "CreateSessionOptions",
    "SetSessionGraphOptimizationLevel",
    "SessionGetInputCount",
    "SessionGetOutputCount",
    "SessionGetInputTypeInfo",
    "SessionGetOutputTypeInfo",
    "SessionGetInputName",
    "SessionGetOutputName",
    "CreateTensorWithDataAsOrtValue",
    "GetTensorMutableData",
    "CastTypeInfoToTensorInfo",
    "GetOnnxTypeFromTypeInfo",
    "GetTensorElementType",
    "GetDimensionsCount",
    "GetDimensions",
    "GetTensorTypeAndShape",
    "CreateCpuMemoryInfo",
    "AllocatorFree",
    "GetAllocatorWithDefaultOptions",
    "ReleaseEnv",
    "ReleaseStatus",
    "ReleaseMemoryInfo",
    "ReleaseSession",
    "ReleaseValue",
    "ReleaseTypeInfo",
    "ReleaseTensorTypeAndShapeInfo",
    "ReleaseSessionOptions",
    "RegisterExecutionProviderLibrary",
    "UnregisterExecutionProviderLibrary",
    "GetEpDevices",
    "SessionOptionsAppendExecutionProvider_V2",
    "HardwareDevice_Type",
    "EpDevice_EpName",
    "EpDevice_Device",
]

print(f"// ORT tag {ORT_TAG}, ORT_API_VERSION", re.search(r"#define ORT_API_VERSION (\d+)", text).group(1))
print(f"// vtable entries: {idx}")
for name in tracked:
    value = indices.get(name)
    cs_name = name.replace("SessionOptionsAppendExecutionProvider_V2", "SessionOptionsAppendExecutionProviderV2")
    cs_name = cs_name.replace("HardwareDevice_Type", "HardwareDeviceType")
    cs_name = cs_name.replace("EpDevice_EpName", "EpDeviceEpName")
    cs_name = cs_name.replace("EpDevice_Device", "EpDeviceDevice")
    print(f"public const int {cs_name} = {value};")
