using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu.Ort;

/// <summary>
/// Dynamic ONNX Runtime C API loader (ORT_API_VERSION 24) for QNN EP sessions.
/// </summary>
internal static unsafe class OrtApiNative
{
    public const uint OrtApiVersion = 24;

    private static OrtApiTable? _api;
    private static nint _library;
    private static string? _loadedVersionString;

    public static OrtApiTable Api => _api ?? throw new InvalidOperationException("ONNX Runtime is not loaded. Call Load first.");

    public static string? LoadedVersionString => _loadedVersionString;

    internal static void Reset()
    {
        _api = null;
        _library = 0;
        _loadedVersionString = null;
    }

    public static void Load(string onnxRuntimeDllPath, ILogger? logger = null)
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

        if (apiBase.GetVersionString != 0)
        {
            var getVersionString =
                Marshal.GetDelegateForFunctionPointer<OrtGetVersionStringDelegate>(apiBase.GetVersionString);
            var versionPtr = getVersionString();
            _loadedVersionString = versionPtr == 0
                ? null
                : Marshal.PtrToStringUTF8(versionPtr);
        }

        var getApi = Marshal.GetDelegateForFunctionPointer<OrtGetApiDelegate>(apiBase.GetApi);
        var apiPtr = getApi(OrtApiVersion);
        if (apiPtr == 0)
        {
            throw new InvalidOperationException(
                $"GetApi({OrtApiVersion}) returned null for ONNX Runtime {_loadedVersionString ?? "unknown"}.");
        }

        _api = new OrtApiTable(apiPtr);
        logger?.LogInformation(
            "Loaded ONNX Runtime {Version} (requested OrtApi version {ApiVersion}) from {Path}",
            _loadedVersionString ?? "(unknown)",
            OrtApiVersion,
            onnxRuntimeDllPath);
    }

    public static void Check(nint status, string context)
    {
        if (status == 0)
        {
            return;
        }

        var api = Api;
        var codeText = "unknown";
        var message = "(no message)";
        try
        {
            var code = api.GetErrorCode(status);
            codeText = code.ToString();
            var messagePtr = api.GetErrorMessage(status);
            if (messagePtr == 0)
            {
                message = "(empty error message)";
            }
            else
            {
                message = Marshal.PtrToStringUTF8(messagePtr) ?? "(empty error message)";
            }
        }
        catch (Exception ex)
        {
            message = $"invalid OrtStatus* 0x{status:X}: {ex.Message}";
        }
        finally
        {
            TryReleaseStatus(api, status);
        }

        throw new InvalidOperationException($"{context}: [{codeText}] {message}");
    }

    private static void TryReleaseStatus(OrtApiTable api, nint status)
    {
        try
        {
            api.ReleaseStatus(status);
        }
        catch
        {
            // Best-effort — status pointer may be corrupt if vtable indices were wrong.
        }
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint OrtGetVersionStringDelegate();

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
    internal delegate int GetErrorCodeDelegate(nint status);

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

internal sealed unsafe class OrtApiTable
{
    private readonly nint _api;

    public OrtApiTable(nint api) => _api = api;

    public OrtNativeDelegates.GetErrorCodeDelegate GetErrorCode =>
        GetFn<OrtNativeDelegates.GetErrorCodeDelegate>(OrtApiIndices.GetErrorCode);

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
