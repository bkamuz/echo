using System.Runtime.InteropServices;

namespace echo.Engines.ParakeetNpu.Ort;

/// <summary>
/// Dynamic ONNX Runtime C API loader (ORT_API_VERSION 24) for QNN EP sessions.
/// </summary>
internal static unsafe class OrtApiNative
{
    public const uint OrtApiVersion = 24;

    private static OrtApiTable? _api;
    private static nint _library;

    public static OrtApiTable Api => _api ?? throw new InvalidOperationException("ONNX Runtime is not loaded. Call Load first.");

    public static void Load(string onnxRuntimeDllPath)
    {
        if (_api is not null)
        {
            return;
        }

        if (!File.Exists(onnxRuntimeDllPath))
        {
            throw new FileNotFoundException($"onnxruntime.dll not found: {onnxRuntimeDllPath}");
        }

        _library = NativeLibrary.Load(onnxRuntimeDllPath);
        if (!NativeLibrary.TryGetExport(_library, "OrtGetApiBase", out var getApiBasePtr))
        {
            throw new InvalidOperationException($"{onnxRuntimeDllPath} does not export OrtGetApiBase.");
        }

        var getApiBase = Marshal.GetDelegateForFunctionPointer<OrtGetApiBaseDelegate>(getApiBasePtr);
        var apiBasePtr = getApiBase();
        if (apiBasePtr == 0)
        {
            throw new InvalidOperationException("OrtGetApiBase returned null.");
        }

        var apiBase = *(OrtApiBase*)apiBasePtr;
        if (apiBase.GetApi == 0)
        {
            throw new InvalidOperationException("OrtApiBase.GetApi is null.");
        }

        var getApi = Marshal.GetDelegateForFunctionPointer<OrtGetApiDelegate>(apiBase.GetApi);
        var apiPtr = getApi(OrtApiVersion);
        if (apiPtr == 0)
        {
            throw new InvalidOperationException($"GetApi({OrtApiVersion}) returned null.");
        }

        _api = new OrtApiTable(apiPtr);
    }

    public static void Check(nint status, string context)
    {
        if (status == 0)
        {
            return;
        }

        var api = Api;
        var messagePtr = api.GetErrorMessage(status);
        var message = messagePtr == 0
            ? "(no message)"
            : Marshal.PtrToStringUTF8(messagePtr) ?? "(no message)";
        api.ReleaseStatus(status);
        throw new InvalidOperationException($"{context}: {message}");
    }

    internal static char[] ToOrtPath(string path)
    {
        var buffer = new char[path.Length + 1];
        path.AsSpan().CopyTo(buffer);
        buffer[^1] = '\0';
        return buffer;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint OrtGetApiBaseDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint OrtGetApiDelegate(uint version);

    [StructLayout(LayoutKind.Sequential)]
    private struct OrtApiBase
    {
        public nint GetApi;
        public nint GetVersionString;
    }
}

internal enum OrtTensorElementType
{
    Undefined = 0,
    Float = 1,
    Int32 = 6,
    Int64 = 7,
    Float16 = 11,
    Uint8 = 2,
}

internal enum OrtLoggingLevel
{
    Verbose = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Fatal = 4,
}

internal enum OrtGraphOptimizationLevel
{
    DisableAll = 0,
    EnableAll = 99,
}

internal enum OrtAllocatorType
{
    DeviceAllocator = 0,
    ArenaAllocator = 1,
}

internal enum OrtMemType
{
    Default = 0,
}

internal enum OrtOnnxType
{
    Unknown = 0,
    Tensor = 1,
}

internal enum OrtHardwareDeviceType
{
    Cpu = 0,
    Gpu = 1,
    Npu = 2,
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtStatusCreateDelegate(int code, nint msg);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtGetErrorMessageDelegate(nint status);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void OrtReleaseStatusDelegate(nint status);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtCreateEnvDelegate(OrtLoggingLevel level, nint logId, nint* env);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void OrtReleaseEnvDelegate(nint env);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtCreateSessionOptionsDelegate(nint* sessionOptions);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void OrtReleaseSessionOptionsDelegate(nint sessionOptions);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtSetSessionGraphOptimizationLevelDelegate(nint sessionOptions, OrtGraphOptimizationLevel level);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtSessionOptionsAppendExecutionProviderV2Delegate(
    nint sessionOptions,
    nint env,
    nint* epDevices,
    nuint numEpDevices,
    nint* epOptionKeys,
    nint* epOptionValues,
    nuint numEpOptions);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtCreateSessionDelegate(nint env, nint modelPath, nint sessionOptions, nint* session);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void OrtReleaseSessionDelegate(nint session);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtCreateCpuMemoryInfoDelegate(OrtAllocatorType allocatorType, OrtMemType memType, nint* memoryInfo);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void OrtReleaseMemoryInfoDelegate(nint memoryInfo);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtCreateTensorWithDataAsOrtValueDelegate(
    nint memoryInfo,
    nint data,
    nuint dataSize,
    nint dimensions,
    nuint dimensionCount,
    OrtTensorElementType elementType,
    nint* value);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void OrtReleaseValueDelegate(nint value);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtRunDelegate(
    nint session,
    nint runOptions,
    nint* inputNames,
    nint* inputValues,
    nuint inputCount,
    nint* outputNames,
    nuint outputCount,
    nint* outputValues);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtGetTensorTypeAndShapeDelegate(nint value, nint* typeAndShape);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void OrtReleaseTensorTypeAndShapeInfoDelegate(nint typeAndShape);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtGetTensorElementTypeDelegate(nint typeAndShape, OrtTensorElementType* elementType);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtGetDimensionsCountDelegate(nint typeAndShape, nuint* count);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtGetDimensionsDelegate(nint typeAndShape, long* dimensions, nuint count);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtGetTensorMutableDataDelegate(nint value, nint* data);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtSessionGetInputCountDelegate(nint session, nuint* count);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtSessionGetOutputCountDelegate(nint session, nuint* count);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtGetAllocatorWithDefaultOptionsDelegate(nint* allocator);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtSessionGetInputNameDelegate(nint session, nuint index, nint allocator, nint* name);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtSessionGetOutputNameDelegate(nint session, nuint index, nint allocator, nint* name);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void OrtReleaseTypeInfoDelegate(nint typeInfo);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtSessionGetInputTypeInfoDelegate(nint session, nuint index, nint* typeInfo);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtSessionGetOutputTypeInfoDelegate(nint session, nuint index, nint* typeInfo);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtGetOnnxTypeFromTypeInfoDelegate(nint typeInfo, OrtOnnxType* onnxType);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtCastTypeInfoToTensorInfoDelegate(nint typeInfo, nint* tensorInfo);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtAllocatorFreeDelegate(nint allocator, nint pointer);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtRegisterExecutionProviderLibraryDelegate(nint env, nint registrationName, nint path);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtUnregisterExecutionProviderLibraryDelegate(nint env, nint registrationName);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtGetEpDevicesDelegate(nint env, nint** epDevices, nuint* numEpDevices);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate OrtHardwareDeviceType OrtHardwareDeviceTypeDelegate(nint device);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtEpDeviceEpNameDelegate(nint epDevice);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint OrtEpDeviceDeviceDelegate(nint epDevice);

internal static class OrtApiIndices
{
    public const int CreateEnv = 3;
    public const int ReleaseEnv = 114;
    public const int CreateSession = 8;
    public const int ReleaseSession = 117;
    public const int Run = 12;
    public const int CreateSessionOptions = 14;
    public const int ReleaseSessionOptions = 122;
    public const int SetSessionGraphOptimizationLevel = 29;
    public const int SessionGetInputCount = 36;
    public const int SessionGetOutputCount = 37;
    public const int SessionGetInputTypeInfo = 39;
    public const int SessionGetOutputTypeInfo = 40;
    public const int SessionGetInputName = 42;
    public const int SessionGetOutputName = 43;
    public const int CreateTensorWithDataAsOrtValue = 58;
    public const int GetTensorMutableData = 61;
    public const int CastTypeInfoToTensorInfo = 65;
    public const int GetOnnxTypeFromTypeInfo = 67;
    public const int GetTensorElementType = 71;
    public const int GetDimensionsCount = 73;
    public const int GetDimensions = 74;
    public const int GetTensorTypeAndShape = 77;
    public const int CreateCpuMemoryInfo = 82;
    public const int AllocatorFree = 90;
    public const int GetAllocatorWithDefaultOptions = 92;
    public const int GetErrorMessage = 2;
    public const int ReleaseStatus = 115;
    public const int ReleaseMemoryInfo = 116;
    public const int ReleaseValue = 118;
    public const int ReleaseTypeInfo = 120;
    public const int ReleaseTensorTypeAndShapeInfo = 121;
    public const int RegisterExecutionProviderLibrary = 384;
    public const int UnregisterExecutionProviderLibrary = 386;
    public const int GetEpDevices = 387;
    public const int SessionOptionsAppendExecutionProviderV2 = 389;
    public const int HardwareDeviceType = 393;
    public const int EpDeviceEpName = 398;
    public const int EpDeviceDevice = 402;
}

internal sealed unsafe class OrtApiTable
{
    private readonly nint _api;

    public OrtApiTable(nint api) => _api = api;

    public OrtGetErrorMessageDelegate GetErrorMessage =>
        GetFn<OrtGetErrorMessageDelegate>(OrtApiIndices.GetErrorMessage);

    public OrtReleaseStatusDelegate ReleaseStatus =>
        GetFn<OrtReleaseStatusDelegate>(OrtApiIndices.ReleaseStatus);

    public OrtCreateEnvDelegate CreateEnv =>
        GetFn<OrtCreateEnvDelegate>(OrtApiIndices.CreateEnv);

    public OrtReleaseEnvDelegate ReleaseEnv =>
        GetFn<OrtReleaseEnvDelegate>(OrtApiIndices.ReleaseEnv);

    public OrtCreateSessionOptionsDelegate CreateSessionOptions =>
        GetFn<OrtCreateSessionOptionsDelegate>(OrtApiIndices.CreateSessionOptions);

    public OrtReleaseSessionOptionsDelegate ReleaseSessionOptions =>
        GetFn<OrtReleaseSessionOptionsDelegate>(OrtApiIndices.ReleaseSessionOptions);

    public OrtSetSessionGraphOptimizationLevelDelegate SetSessionGraphOptimizationLevel =>
        GetFn<OrtSetSessionGraphOptimizationLevelDelegate>(OrtApiIndices.SetSessionGraphOptimizationLevel);

    public OrtSessionOptionsAppendExecutionProviderV2Delegate SessionOptionsAppendExecutionProviderV2 =>
        GetFn<OrtSessionOptionsAppendExecutionProviderV2Delegate>(OrtApiIndices.SessionOptionsAppendExecutionProviderV2);

    public OrtCreateSessionDelegate CreateSession =>
        GetFn<OrtCreateSessionDelegate>(OrtApiIndices.CreateSession);

    public OrtReleaseSessionDelegate ReleaseSession =>
        GetFn<OrtReleaseSessionDelegate>(OrtApiIndices.ReleaseSession);

    public OrtCreateCpuMemoryInfoDelegate CreateCpuMemoryInfo =>
        GetFn<OrtCreateCpuMemoryInfoDelegate>(OrtApiIndices.CreateCpuMemoryInfo);

    public OrtReleaseMemoryInfoDelegate ReleaseMemoryInfo =>
        GetFn<OrtReleaseMemoryInfoDelegate>(OrtApiIndices.ReleaseMemoryInfo);

    public OrtCreateTensorWithDataAsOrtValueDelegate CreateTensorWithDataAsOrtValue =>
        GetFn<OrtCreateTensorWithDataAsOrtValueDelegate>(OrtApiIndices.CreateTensorWithDataAsOrtValue);

    public OrtReleaseValueDelegate ReleaseValue =>
        GetFn<OrtReleaseValueDelegate>(OrtApiIndices.ReleaseValue);

    public OrtRunDelegate Run =>
        GetFn<OrtRunDelegate>(OrtApiIndices.Run);

    public OrtGetTensorTypeAndShapeDelegate GetTensorTypeAndShape =>
        GetFn<OrtGetTensorTypeAndShapeDelegate>(OrtApiIndices.GetTensorTypeAndShape);

    public OrtReleaseTensorTypeAndShapeInfoDelegate ReleaseTensorTypeAndShapeInfo =>
        GetFn<OrtReleaseTensorTypeAndShapeInfoDelegate>(OrtApiIndices.ReleaseTensorTypeAndShapeInfo);

    public OrtGetTensorElementTypeDelegate GetTensorElementType =>
        GetFn<OrtGetTensorElementTypeDelegate>(OrtApiIndices.GetTensorElementType);

    public OrtGetDimensionsCountDelegate GetDimensionsCount =>
        GetFn<OrtGetDimensionsCountDelegate>(OrtApiIndices.GetDimensionsCount);

    public OrtGetDimensionsDelegate GetDimensions =>
        GetFn<OrtGetDimensionsDelegate>(OrtApiIndices.GetDimensions);

    public OrtGetTensorMutableDataDelegate GetTensorMutableData =>
        GetFn<OrtGetTensorMutableDataDelegate>(OrtApiIndices.GetTensorMutableData);

    public OrtSessionGetInputCountDelegate SessionGetInputCount =>
        GetFn<OrtSessionGetInputCountDelegate>(OrtApiIndices.SessionGetInputCount);

    public OrtSessionGetOutputCountDelegate SessionGetOutputCount =>
        GetFn<OrtSessionGetOutputCountDelegate>(OrtApiIndices.SessionGetOutputCount);

    public OrtGetAllocatorWithDefaultOptionsDelegate GetAllocatorWithDefaultOptions =>
        GetFn<OrtGetAllocatorWithDefaultOptionsDelegate>(OrtApiIndices.GetAllocatorWithDefaultOptions);

    public OrtSessionGetInputNameDelegate SessionGetInputName =>
        GetFn<OrtSessionGetInputNameDelegate>(OrtApiIndices.SessionGetInputName);

    public OrtSessionGetOutputNameDelegate SessionGetOutputName =>
        GetFn<OrtSessionGetOutputNameDelegate>(OrtApiIndices.SessionGetOutputName);

    public OrtReleaseTypeInfoDelegate ReleaseTypeInfo =>
        GetFn<OrtReleaseTypeInfoDelegate>(OrtApiIndices.ReleaseTypeInfo);

    public OrtSessionGetInputTypeInfoDelegate SessionGetInputTypeInfo =>
        GetFn<OrtSessionGetInputTypeInfoDelegate>(OrtApiIndices.SessionGetInputTypeInfo);

    public OrtSessionGetOutputTypeInfoDelegate SessionGetOutputTypeInfo =>
        GetFn<OrtSessionGetOutputTypeInfoDelegate>(OrtApiIndices.SessionGetOutputTypeInfo);

    public OrtGetOnnxTypeFromTypeInfoDelegate GetOnnxTypeFromTypeInfo =>
        GetFn<OrtGetOnnxTypeFromTypeInfoDelegate>(OrtApiIndices.GetOnnxTypeFromTypeInfo);

    public OrtCastTypeInfoToTensorInfoDelegate CastTypeInfoToTensorInfo =>
        GetFn<OrtCastTypeInfoToTensorInfoDelegate>(OrtApiIndices.CastTypeInfoToTensorInfo);

    public OrtAllocatorFreeDelegate AllocatorFree =>
        GetFn<OrtAllocatorFreeDelegate>(OrtApiIndices.AllocatorFree);

    public OrtRegisterExecutionProviderLibraryDelegate RegisterExecutionProviderLibrary =>
        GetFn<OrtRegisterExecutionProviderLibraryDelegate>(OrtApiIndices.RegisterExecutionProviderLibrary);

    public OrtUnregisterExecutionProviderLibraryDelegate UnregisterExecutionProviderLibrary =>
        GetFn<OrtUnregisterExecutionProviderLibraryDelegate>(OrtApiIndices.UnregisterExecutionProviderLibrary);

    public OrtGetEpDevicesDelegate GetEpDevices =>
        GetFn<OrtGetEpDevicesDelegate>(OrtApiIndices.GetEpDevices);

    public OrtHardwareDeviceTypeDelegate HardwareDeviceType =>
        GetFn<OrtHardwareDeviceTypeDelegate>(OrtApiIndices.HardwareDeviceType);

    public OrtEpDeviceEpNameDelegate EpDeviceEpName =>
        GetFn<OrtEpDeviceEpNameDelegate>(OrtApiIndices.EpDeviceEpName);

    public OrtEpDeviceDeviceDelegate EpDeviceDevice =>
        GetFn<OrtEpDeviceDeviceDelegate>(OrtApiIndices.EpDeviceDevice);

    private T GetFn<T>(int index) where T : Delegate
    {
        var fnPtr = *(nint*)((byte*)_api + index * sizeof(nint));
        if (fnPtr == 0)
        {
            throw new InvalidOperationException($"OrtApi function at index {index} is null.");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(fnPtr);
    }
}
