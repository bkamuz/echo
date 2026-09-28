namespace echo.Engines.ParakeetNpu.Ort;

/// <summary>
/// OrtApi vtable indices for ORT 1.24.x (ORT_API_VERSION 24), derived from
/// include/onnxruntime/core/session/onnxruntime_c_api.h at tag v1.24.4.
/// Regenerate with: tools/generate-ort-api-indices.py
/// </summary>
internal static class OrtApiIndices
{
    public const int GetErrorCode = 1;
    public const int GetErrorMessage = 2;
    public const int CreateEnv = 3;
    public const int CreateSession = 7;
    public const int Run = 9;
    public const int CreateSessionOptions = 10;
    public const int SetSessionGraphOptimizationLevel = 23;
    public const int SessionGetInputCount = 30;
    public const int SessionGetOutputCount = 31;
    public const int SessionGetInputTypeInfo = 33;
    public const int SessionGetOutputTypeInfo = 34;
    public const int SessionGetInputName = 36;
    public const int SessionGetOutputName = 37;
    public const int CreateTensorWithDataAsOrtValue = 49;
    public const int GetTensorMutableData = 51;
    public const int CastTypeInfoToTensorInfo = 55;
    public const int GetOnnxTypeFromTypeInfo = 56;
    public const int GetTensorElementType = 60;
    public const int GetDimensionsCount = 61;
    public const int GetDimensions = 62;
    public const int GetTensorTypeAndShape = 65;
    public const int CreateCpuMemoryInfo = 69;
    public const int AllocatorFree = 76;
    public const int GetAllocatorWithDefaultOptions = 78;
    public const int ReleaseEnv = 92;
    public const int ReleaseStatus = 93;
    public const int ReleaseMemoryInfo = 94;
    public const int ReleaseSession = 95;
    public const int ReleaseValue = 96;
    public const int ReleaseTypeInfo = 98;
    public const int ReleaseTensorTypeAndShapeInfo = 99;
    public const int ReleaseSessionOptions = 100;
    public const int RegisterExecutionProviderLibrary = 301;
    public const int UnregisterExecutionProviderLibrary = 302;
    public const int GetEpDevices = 303;
    public const int SessionOptionsAppendExecutionProviderV2 = 304;
    public const int HardwareDeviceType = 307;
    public const int EpDeviceEpName = 312;
    public const int EpDeviceDevice = 316;
}
