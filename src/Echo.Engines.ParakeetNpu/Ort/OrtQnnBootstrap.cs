using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu.Ort;

/// <summary>
/// Initializes ORT + QNN runtime DLLs and registers the QNN execution provider.
/// </summary>
internal static unsafe class OrtQnnBootstrap
{
    private const string QnnProviderName = "QNNExecutionProvider";
    private static readonly object Gate = new();
    private static readonly string[] PreloadDlls =
    [
        "QnnSystem.dll",
        "QnnHtpPrepare.dll",
        "QnnHtpNetRunExtensions.dll",
        "QnnHtp.dll",
    ];

    private static string? _runtimeDir;
    private static nint _env;
    private static int _providerLeases;
    private static bool _providerRegistered;
    private static readonly List<nint> _preloadedModules = [];

    [ThreadStatic]
    private static ComApartmentHelper.ComApartmentLease? _comApartment;

    public static nint EnvHandle
    {
        get
        {
            EnsureInitialized();
            return _env;
        }
    }

    public static string RuntimeDirectory
    {
        get
        {
            EnsureInitialized();
            return _runtimeDir!;
        }
    }

    public static void Initialize(string runtimeDir, ILogger? logger = null)
    {
        lock (Gate)
        {
            if (_env != 0)
            {
                return;
            }

            _runtimeDir = Path.GetFullPath(runtimeDir);
            VerifyRequiredFiles(_runtimeDir);
            Environment.SetEnvironmentVariable("ORT_DYLIB_PATH", Path.Combine(_runtimeDir, "onnxruntime.dll"));
            OrtApiNative.Load(Path.Combine(_runtimeDir, "onnxruntime.dll"));
            PreloadQnnRuntime(_runtimeDir, logger);
            CreateEnvironment(logger);
        }
    }

    public static QnnProviderLease AcquireQnnProvider(ILogger? logger = null)
    {
        EnsureInitialized();
        EnsureComApartment(logger);

        lock (Gate)
        {
            if (!_providerRegistered)
            {
                var providerPath = Path.Combine(_runtimeDir!, "onnxruntime_providers_qnn.dll");
                if (!File.Exists(providerPath))
                {
                    throw new FileNotFoundException($"QNN provider DLL not found: {providerPath}");
                }

                var api = OrtApiNative.Api;
                var registrationName = Marshal.StringToCoTaskMemUTF8(QnnProviderName);
                var widePath = Marshal.StringToCoTaskMemUni(providerPath);
                try
                {
                    OrtApiNative.Check(
                        api.RegisterExecutionProviderLibrary(_env, registrationName, widePath),
                        "RegisterExecutionProviderLibrary");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(registrationName);
                    Marshal.FreeCoTaskMem(widePath);
                }

                _providerRegistered = true;
                logger?.LogInformation(
                    "QNN execution provider registered from {ProviderPath}",
                    providerPath);
            }

            _providerLeases = checked(_providerLeases + 1);
            return new QnnProviderLease();
        }
    }

    public static IReadOnlyList<nint> EnumerateQnnNpuDevices(ILogger? logger = null)
    {
        EnsureInitialized();
        EnsureComApartment(logger);

        var api = OrtApiNative.Api;
        nint* devicesPtr = null;
        nuint count = 0;
        OrtApiNative.Check(api.GetEpDevices(_env, &devicesPtr, &count), "GetEpDevices");
        if (count == 0 || devicesPtr == null)
        {
            logger?.LogWarning("GetEpDevices returned no execution-provider devices.");
            return Array.Empty<nint>();
        }

        var qnnDevices = new List<nint>();
        for (nuint i = 0; i < count; i++)
        {
            var device = devicesPtr[i];
            if (device == 0)
            {
                continue;
            }

            var epNamePtr = api.EpDeviceEpName(device);
            if (epNamePtr == 0)
            {
                continue;
            }

            var epName = Marshal.PtrToStringUTF8(epNamePtr);
            var hardwarePtr = api.EpDeviceDevice(device);
            if (hardwarePtr == 0)
            {
                continue;
            }

            var hardwareType = api.HardwareDeviceType(hardwarePtr);
            if (hardwareType == OrtHardwareDeviceType.Npu &&
                string.Equals(epName, QnnProviderName, StringComparison.Ordinal))
            {
                qnnDevices.Add(device);
                logger?.LogInformation("Found QNN NPU device: EP={EpName}, hardware={HardwareType}", epName, hardwareType);
            }
        }

        if (qnnDevices.Count == 0)
        {
            logger?.LogWarning("No QNN NPU devices discovered by ORT.");
        }
        else
        {
            logger?.LogInformation("Discovered {Count} QNN NPU device(s) for HTP sessions.", qnnDevices.Count);
        }

        return qnnDevices;
    }

    internal static void ReleaseProviderLease(ILogger? logger = null)
    {
        lock (Gate)
        {
            if (_providerLeases == 0)
            {
                throw new InvalidOperationException("QNN provider lease count underflow.");
            }

            _providerLeases--;
            if (_providerLeases == 0 && _providerRegistered)
            {
                var api = OrtApiNative.Api;
                var registrationName = Marshal.StringToCoTaskMemUTF8(QnnProviderName);
                try
                {
                    OrtApiNative.Check(
                        api.UnregisterExecutionProviderLibrary(_env, registrationName),
                        "UnregisterExecutionProviderLibrary");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(registrationName);
                }

                _providerRegistered = false;
                logger?.LogInformation("QNN execution provider unregistered after final session.");
            }
        }
    }

    public static void EnsureComApartment(ILogger? logger = null)
    {
        if (_comApartment is null)
        {
            _comApartment = ComApartmentHelper.EnsureInitialized(logger);
        }
    }

    private static void EnsureInitialized()
    {
        if (_env == 0)
        {
            throw new InvalidOperationException("QNN runtime is not initialized. Call Initialize first.");
        }
    }

    private static void CreateEnvironment(ILogger? logger)
    {
        var api = OrtApiNative.Api;
        var logId = Marshal.StringToCoTaskMemUTF8("echo-parakeet-npu");
        nint env = 0;
        try
        {
            OrtApiNative.Check(api.CreateEnv(OrtLoggingLevel.Warning, logId, &env), "CreateEnv");
            _env = env;
            logger?.LogInformation("ORT environment created for Parakeet NPU path.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(logId);
        }
    }

    private static void VerifyRequiredFiles(string runtimeDir)
    {
        var manifest = ManifestLoader.LoadRuntimeManifest();
        var missing = manifest.RequiredFiles
            .Where(file => !QnnRuntimePaths.IsFilePresent(Path.Combine(runtimeDir, file)))
            .ToList();
        if (missing.Count > 0)
        {
            throw new FileNotFoundException(
                $"QNN runtime is missing required files in {runtimeDir}: {string.Join(", ", missing)}. "
                + "Run Download in Settings or switch to NPU to fetch the QNN runtime.");
        }
    }

    private static void PreloadQnnRuntime(string runtimeDir, ILogger? logger)
    {
        if (_preloadedModules.Count > 0)
        {
            return;
        }

        foreach (var name in PreloadDlls)
        {
            var path = Path.Combine(runtimeDir, name);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required QNN runtime DLL not found: {path}");
            }

            var module = NativeLibrary.Load(path);
            _preloadedModules.Add(module);
            logger?.LogDebug("Preloaded QNN runtime DLL {Name}", name);
        }
    }

}

internal sealed class QnnProviderLease : IDisposable
{
    private bool _active = true;

    public void Dispose()
    {
        if (!_active)
        {
            return;
        }

        OrtQnnBootstrap.ReleaseProviderLease();
        _active = false;
    }
}
