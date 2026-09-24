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

        _library = QnnNativeLoader.LoadLibrary(onnxRuntimeDllPath);
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

internal static unsafe class OrtNativeDelegates
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint StatusCreateDelegate(int code, nint msg);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint GetErrorMessageDelegate(nint status);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void ReleaseStatusDelegate(nint status);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint CreateEnvDelegate(OrtLoggingLevel level, nint logId, nint* env);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void ReleaseEnvDelegate(nint env);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint CreateSessionOptionsDelegate(nint* sessionOptions);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void ReleaseSessionOptionsDelegate(nint sessionOptions);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SetSessionGraphOptimizationLevelDelegate(nint sessionOptions, OrtGraphOptimizationLevel level);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SessionOptionsAppendExecutionProviderV2Delegate(
        nint sessionOptions,
        nint env,
        nint* epDevices,
        nuint numEpDevices,
        nint* epOptionKeys,
        nint* epOptionValues,
        nuint numEpOptions);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint CreateSessionDelegate(nint env, nint modelPath, nint sessionOptions, nint* session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void ReleaseSessionDelegate(nint session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint CreateCpuMemoryInfoDelegate(OrtAllocatorType allocatorType, OrtMemType memType, nint* memoryInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void ReleaseMemoryInfoDelegate(nint memoryInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint CreateTensorWithDataAsOrtValueDelegate(
        nint memoryInfo,
        nint data,
        nuint dataSize,
        nint dimensions,
        nuint dimensionCount,
        OrtTensorElementType elementType,
        nint* value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void ReleaseValueDelegate(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint RunDelegate(
        nint session,
        nint runOptions,
        nint* inputNames,
        nint* inputValues,
        nuint inputCount,
        nint* outputNames,
        nuint outputCount,
        nint* outputValues);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint GetTensorTypeAndShapeDelegate(nint value, nint* typeAndShape);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void ReleaseTensorTypeAndShapeInfoDelegate(nint typeAndShape);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint GetTensorElementTypeDelegate(nint typeAndShape, OrtTensorElementType* elementType);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint GetDimensionsCountDelegate(nint typeAndShape, nuint* count);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint GetDimensionsDelegate(nint typeAndShape, long* dimensions, nuint count);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint GetTensorMutableDataDelegate(nint value, nint* data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SessionGetInputCountDelegate(nint session, nuint* count);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SessionGetOutputCountDelegate(nint session, nuint* count);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint GetAllocatorWithDefaultOptionsDelegate(nint* allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SessionGetInputNameDelegate(nint session, nuint index, nint allocator, nint* name);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SessionGetOutputNameDelegate(nint session, nuint index, nint allocator, nint* name);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void ReleaseTypeInfoDelegate(nint typeInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SessionGetInputTypeInfoDelegate(nint session, nuint index, nint* typeInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SessionGetOutputTypeInfoDelegate(nint session, nuint index, nint* typeInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint GetOnnxTypeFromTypeInfoDelegate(nint typeInfo, OrtOnnxType* onnxType);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint CastTypeInfoToTensorInfoDelegate(nint typeInfo, nint* tensorInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint AllocatorFreeDelegate(nint allocator, nint pointer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint RegisterExecutionProviderLibraryDelegate(nint env, nint registrationName, nint path);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint UnregisterExecutionProviderLibraryDelegate(nint env, nint registrationName);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint GetEpDevicesDelegate(nint env, nint** epDevices, nuint* numEpDevices);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate OrtHardwareDeviceType HardwareDeviceTypeDelegate(nint device);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint EpDeviceEpNameDelegate(nint epDevice);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint EpDeviceDeviceDelegate(nint epDevice);
}

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

    public OrtNativeDelegates.GetErrorMessageDelegate GetErrorMessage =>
        GetFn<OrtNativeDelegates.GetErrorMessageDelegate>(OrtApiIndices.GetErrorMessage);

    public OrtNativeDelegates.ReleaseStatusDelegate ReleaseStatus =>
        GetFn<OrtNativeDelegates.ReleaseStatusDelegate>(OrtApiIndices.ReleaseStatus);

    public OrtNativeDelegates.CreateEnvDelegate CreateEnv =>
        GetFn<OrtNativeDelegates.CreateEnvDelegate>(OrtApiIndices.CreateEnv);

    public OrtNativeDelegates.ReleaseEnvDelegate ReleaseEnv =>
        GetFn<OrtNativeDelegates.ReleaseEnvDelegate>(OrtApiIndices.ReleaseEnv);

    public OrtNativeDelegates.CreateSessionOptionsDelegate CreateSessionOptions =>
        GetFn<OrtNativeDelegates.CreateSessionOptionsDelegate>(OrtApiIndices.CreateSessionOptions);

    public OrtNativeDelegates.ReleaseSessionOptionsDelegate ReleaseSessionOptions =>
        GetFn<OrtNativeDelegates.ReleaseSessionOptionsDelegate>(OrtApiIndices.ReleaseSessionOptions);

    public OrtNativeDelegates.SetSessionGraphOptimizationLevelDelegate SetSessionGraphOptimizationLevel =>
        GetFn<OrtNativeDelegates.SetSessionGraphOptimizationLevelDelegate>(OrtApiIndices.SetSessionGraphOptimizationLevel);

    public OrtNativeDelegates.SessionOptionsAppendExecutionProviderV2Delegate SessionOptionsAppendExecutionProviderV2 =>
        GetFn<OrtNativeDelegates.SessionOptionsAppendExecutionProviderV2Delegate>(OrtApiIndices.SessionOptionsAppendExecutionProviderV2);

    public OrtNativeDelegates.CreateSessionDelegate CreateSession =>
        GetFn<OrtNativeDelegates.CreateSessionDelegate>(OrtApiIndices.CreateSession);

    public OrtNativeDelegates.ReleaseSessionDelegate ReleaseSession =>
        GetFn<OrtNativeDelegates.ReleaseSessionDelegate>(OrtApiIndices.ReleaseSession);

    public OrtNativeDelegates.CreateCpuMemoryInfoDelegate CreateCpuMemoryInfo =>
        GetFn<OrtNativeDelegates.CreateCpuMemoryInfoDelegate>(OrtApiIndices.CreateCpuMemoryInfo);

    public OrtNativeDelegates.ReleaseMemoryInfoDelegate ReleaseMemoryInfo =>
        GetFn<OrtNativeDelegates.ReleaseMemoryInfoDelegate>(OrtApiIndices.ReleaseMemoryInfo);

    public OrtNativeDelegates.CreateTensorWithDataAsOrtValueDelegate CreateTensorWithDataAsOrtValue =>
        GetFn<OrtNativeDelegates.CreateTensorWithDataAsOrtValueDelegate>(OrtApiIndices.CreateTensorWithDataAsOrtValue);

    public OrtNativeDelegates.ReleaseValueDelegate ReleaseValue =>
        GetFn<OrtNativeDelegates.ReleaseValueDelegate>(OrtApiIndices.ReleaseValue);

    public OrtNativeDelegates.RunDelegate Run =>
        GetFn<OrtNativeDelegates.RunDelegate>(OrtApiIndices.Run);

    public OrtNativeDelegates.GetTensorTypeAndShapeDelegate GetTensorTypeAndShape =>
        GetFn<OrtNativeDelegates.GetTensorTypeAndShapeDelegate>(OrtApiIndices.GetTensorTypeAndShape);

    public OrtNativeDelegates.ReleaseTensorTypeAndShapeInfoDelegate ReleaseTensorTypeAndShapeInfo =>
        GetFn<OrtNativeDelegates.ReleaseTensorTypeAndShapeInfoDelegate>(OrtApiIndices.ReleaseTensorTypeAndShapeInfo);

    public OrtNativeDelegates.GetTensorElementTypeDelegate GetTensorElementType =>
        GetFn<OrtNativeDelegates.GetTensorElementTypeDelegate>(OrtApiIndices.GetTensorElementType);

    public OrtNativeDelegates.GetDimensionsCountDelegate GetDimensionsCount =>
        GetFn<OrtNativeDelegates.GetDimensionsCountDelegate>(OrtApiIndices.GetDimensionsCount);

    public OrtNativeDelegates.GetDimensionsDelegate GetDimensions =>
        GetFn<OrtNativeDelegates.GetDimensionsDelegate>(OrtApiIndices.GetDimensions);

    public OrtNativeDelegates.GetTensorMutableDataDelegate GetTensorMutableData =>
        GetFn<OrtNativeDelegates.GetTensorMutableDataDelegate>(OrtApiIndices.GetTensorMutableData);

    public OrtNativeDelegates.SessionGetInputCountDelegate SessionGetInputCount =>
        GetFn<OrtNativeDelegates.SessionGetInputCountDelegate>(OrtApiIndices.SessionGetInputCount);

    public OrtNativeDelegates.SessionGetOutputCountDelegate SessionGetOutputCount =>
        GetFn<OrtNativeDelegates.SessionGetOutputCountDelegate>(OrtApiIndices.SessionGetOutputCount);

    public OrtNativeDelegates.GetAllocatorWithDefaultOptionsDelegate GetAllocatorWithDefaultOptions =>
        GetFn<OrtNativeDelegates.GetAllocatorWithDefaultOptionsDelegate>(OrtApiIndices.GetAllocatorWithDefaultOptions);

    public OrtNativeDelegates.SessionGetInputNameDelegate SessionGetInputName =>
        GetFn<OrtNativeDelegates.SessionGetInputNameDelegate>(OrtApiIndices.SessionGetInputName);

    public OrtNativeDelegates.SessionGetOutputNameDelegate SessionGetOutputName =>
        GetFn<OrtNativeDelegates.SessionGetOutputNameDelegate>(OrtApiIndices.SessionGetOutputName);

    public OrtNativeDelegates.ReleaseTypeInfoDelegate ReleaseTypeInfo =>
        GetFn<OrtNativeDelegates.ReleaseTypeInfoDelegate>(OrtApiIndices.ReleaseTypeInfo);

    public OrtNativeDelegates.SessionGetInputTypeInfoDelegate SessionGetInputTypeInfo =>
        GetFn<OrtNativeDelegates.SessionGetInputTypeInfoDelegate>(OrtApiIndices.SessionGetInputTypeInfo);

    public OrtNativeDelegates.SessionGetOutputTypeInfoDelegate SessionGetOutputTypeInfo =>
        GetFn<OrtNativeDelegates.SessionGetOutputTypeInfoDelegate>(OrtApiIndices.SessionGetOutputTypeInfo);

    public OrtNativeDelegates.GetOnnxTypeFromTypeInfoDelegate GetOnnxTypeFromTypeInfo =>
        GetFn<OrtNativeDelegates.GetOnnxTypeFromTypeInfoDelegate>(OrtApiIndices.GetOnnxTypeFromTypeInfo);

    public OrtNativeDelegates.CastTypeInfoToTensorInfoDelegate CastTypeInfoToTensorInfo =>
        GetFn<OrtNativeDelegates.CastTypeInfoToTensorInfoDelegate>(OrtApiIndices.CastTypeInfoToTensorInfo);

    public OrtNativeDelegates.AllocatorFreeDelegate AllocatorFree =>
        GetFn<OrtNativeDelegates.AllocatorFreeDelegate>(OrtApiIndices.AllocatorFree);

    public OrtNativeDelegates.RegisterExecutionProviderLibraryDelegate RegisterExecutionProviderLibrary =>
        GetFn<OrtNativeDelegates.RegisterExecutionProviderLibraryDelegate>(OrtApiIndices.RegisterExecutionProviderLibrary);

    public OrtNativeDelegates.UnregisterExecutionProviderLibraryDelegate UnregisterExecutionProviderLibrary =>
        GetFn<OrtNativeDelegates.UnregisterExecutionProviderLibraryDelegate>(OrtApiIndices.UnregisterExecutionProviderLibrary);

    public OrtNativeDelegates.GetEpDevicesDelegate GetEpDevices =>
        GetFn<OrtNativeDelegates.GetEpDevicesDelegate>(OrtApiIndices.GetEpDevices);

    public OrtNativeDelegates.HardwareDeviceTypeDelegate HardwareDeviceType =>
        GetFn<OrtNativeDelegates.HardwareDeviceTypeDelegate>(OrtApiIndices.HardwareDeviceType);

    public OrtNativeDelegates.EpDeviceEpNameDelegate EpDeviceEpName =>
        GetFn<OrtNativeDelegates.EpDeviceEpNameDelegate>(OrtApiIndices.EpDeviceEpName);

    public OrtNativeDelegates.EpDeviceDeviceDelegate EpDeviceDevice =>
        GetFn<OrtNativeDelegates.EpDeviceDeviceDelegate>(OrtApiIndices.EpDeviceDevice);

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
