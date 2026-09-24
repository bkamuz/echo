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
    private static readonly string[] SharedPreloadDlls =
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
    private static HtpHardwareProfile? _htpProfile;
    private static readonly List<nint> _preloadedModules = [];

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

    public static HtpHardwareProfile HtpProfile
    {
        get
        {
            EnsureInitialized();
            return _htpProfile ?? HtpHardwareProfile.Resolve();
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
            _htpProfile = HtpHardwareProfile.Resolve();
            logger?.LogInformation(
                "Parakeet QNN HTP profile: generation={Generation}, htp_arch={HtpArch}, soc_model={SocModel}, contextTarget={ContextTarget}",
                _htpProfile.Generation,
                _htpProfile.HtpArch,
                _htpProfile.SocModel,
                _htpProfile.ContextTarget);

            QnnNativeLoader.PrepareSearchPath(_runtimeDir, logger);
            VerifyRequiredFiles(_runtimeDir, _htpProfile, logger);
            OrtApiNative.Load(Path.Combine(_runtimeDir, "onnxruntime.dll"));
            PreloadQnnRuntime(_runtimeDir, logger);
            CreateEnvironment(logger);
        }
    }

    public static QnnProviderLease AcquireQnnProvider(ILogger? logger = null)
    {
        EnsureInitialized();
        return QnnNativeWorker.Run(() => AcquireQnnProviderCore(logger), logger);
    }

    public static IReadOnlyList<nint> EnumerateQnnNpuDevices(ILogger? logger = null)
    {
        EnsureInitialized();
        return QnnNativeWorker.Run(() => EnumerateQnnNpuDevicesCore(logger), logger);
    }

    internal static void ReleaseProviderLease(ILogger? logger = null)
    {
        QnnNativeWorker.Run(() => ReleaseProviderLeaseCore(logger), logger);
    }

    public static void EnsureComApartment(ILogger? logger = null)
    {
        QnnNativeWorker.Run(() =>
        {
            ComApartmentHelper.EnsureInitialized(logger);
            return 0;
        }, logger);
    }

    private static QnnProviderLease AcquireQnnProviderCore(ILogger? logger)
    {
        ComApartmentHelper.EnsureInitialized(logger);

        lock (Gate)
        {
            if (!_providerRegistered)
            {
                var providerPath = Path.Combine(_runtimeDir!, "onnxruntime_providers_qnn.dll");
                if (!File.Exists(providerPath))
                {
                    throw new FileNotFoundException($"QNN provider DLL not found: {providerPath}");
                }

                logger?.LogInformation(
                    "Registering QNN execution provider from {ProviderPath} (HTP arch={HtpArch}, soc_model={SocModel})",
                    providerPath,
                    HtpProfile.HtpArch,
                    HtpProfile.SocModel);

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

    private static IReadOnlyList<nint> EnumerateQnnNpuDevicesCore(ILogger? logger)
    {
        ComApartmentHelper.EnsureInitialized(logger);

        logger?.LogInformation("Calling ORT GetEpDevices to enumerate QNN NPU hardware");

        var api = OrtApiNative.Api;
        nint* devicesPtr = null;
        nuint count = 0;
        OrtApiNative.Check(api.GetEpDevices(_env, &devicesPtr, &count), "GetEpDevices");
        if (count == 0 || devicesPtr == null)
        {
            logger?.LogWarning("GetEpDevices returned no execution-provider devices.");
            return Array.Empty<nint>();
        }

        logger?.LogInformation("GetEpDevices returned {Count} execution-provider device(s)", count);

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

    private static void ReleaseProviderLeaseCore(ILogger? logger)
    {
        ComApartmentHelper.EnsureInitialized(logger);

        lock (Gate)
        {
            if (_providerLeases == 0)
            {
                throw new InvalidOperationException("QNN provider lease count underflow.");
            }

            _providerLeases--;
            if (_providerLeases == 0 && _providerRegistered)
            {
                logger?.LogInformation("Unregistering QNN execution provider after final session");

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
            logger?.LogInformation("Creating ORT environment for Parakeet NPU path");
            OrtApiNative.Check(api.CreateEnv(OrtLoggingLevel.Warning, logId, &env), "CreateEnv");
            _env = env;
            logger?.LogInformation("ORT environment created for Parakeet NPU path.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(logId);
        }
    }

    private static void VerifyRequiredFiles(string runtimeDir, HtpHardwareProfile profile, ILogger? logger)
    {
        var manifest = ManifestLoader.LoadRuntimeManifest();
        var required = profile.RequiredRuntimeFiles();
        var missing = required
            .Where(file => !QnnRuntimePaths.IsFilePresent(Path.Combine(runtimeDir, file)))
            .ToList();
        if (missing.Count > 0)
        {
            throw new FileNotFoundException(
                $"QNN runtime is missing required files for {profile.ContextTarget} in {runtimeDir}: "
                + $"{string.Join(", ", missing)}. "
                + "Run Download in Settings or switch to NPU to fetch the QNN runtime.");
        }

        var manifestMissing = manifest.RequiredFiles
            .Where(file => !QnnRuntimePaths.IsFilePresent(Path.Combine(runtimeDir, file)))
            .ToList();
        if (manifestMissing.Count > 0)
        {
            logger?.LogWarning(
                "QNN runtime is missing optional manifest files (may be needed on other Snapdragon generations): {MissingFiles}",
                string.Join(", ", manifestMissing));
        }
    }

    private static void PreloadQnnRuntime(string runtimeDir, ILogger? logger)
    {
        if (_preloadedModules.Count > 0)
        {
            return;
        }

        // Shared QNN backend DLLs only. HTP stub/skel/cat are loaded by QnnHtp.dll at runtime
        // (stub via Windows loader, skel via ADSP_LIBRARY_PATH) — do not preload them here.
        foreach (var name in SharedPreloadDlls)
        {
            PreloadDll(runtimeDir, name, logger);
        }
    }

    private static void PreloadDll(string runtimeDir, string name, ILogger? logger)
    {
        var path = Path.Combine(runtimeDir, name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required QNN runtime DLL not found: {path}", path);
        }

        try
        {
            var module = QnnNativeLoader.LoadLibrary(path, logger);
            _preloadedModules.Add(module);
            logger?.LogDebug("Preloaded QNN runtime DLL {Name} from {Path}", name, path);
        }
        catch (DllNotFoundException ex)
        {
            throw new DllNotFoundException(
                $"Failed to preload QNN runtime DLL '{name}' from {path}. {ex.Message}",
                ex);
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
