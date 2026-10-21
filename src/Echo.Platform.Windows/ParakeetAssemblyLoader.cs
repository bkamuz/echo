using System.Reflection;
using echo.Abstractions.Engines;
using echo.Core.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

/// <summary>
/// Loads Echo.Engines.ParakeetNpu on first use via reflection so Microsoft.ML.OnnxRuntime
/// is not pulled into the startup assembly graph.
/// </summary>
internal static class ParakeetAssemblyLoader
{
    public const string QnnHttpClientName = "echo-parakeet-qnn";
    public const string ModelHttpClientName = "echo-parakeet-model";
    public const string WhisperNpuHttpClientName = "echo-whisper-npu";
    public const string WhisperNpuEngineId = "whisper_npu";
    private const string AssemblyName = "Echo.Engines.ParakeetNpu";

    public static bool IsSupported =>
        OperatingSystem.IsWindows()
        && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            is System.Runtime.InteropServices.Architecture.Arm64
            or System.Runtime.InteropServices.Architecture.Arm;

    public static void RegisterEngine(IServiceCollection services)
    {
        if (!IsSupported)
        {
            return;
        }

        services.AddHttpClient(QnnHttpClientName, ConfigureParakeetHttpClient);
        services.AddHttpClient(ModelHttpClientName, ConfigureParakeetHttpClient);
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "parakeet_npu",
            Factory = CreateRoutedEngine,
        });

        services.AddHttpClient(WhisperNpuHttpClientName, client =>
        {
            ConfigureParakeetHttpClient(client);
            // ~2 GB AI Hub archive; the downloader resumes with Range requests on failure.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton(new EngineRegistration
        {
            EngineId = WhisperNpuEngineId,
            Factory = sp => new WhisperNpuWindowsEngine(
                sp,
                (ILogger)CreateTypedLogger(sp.GetRequiredService<ILoggerFactory>(), typeof(WhisperNpuWindowsEngine))),
        });
    }

    private static object WhisperRuntimeProfile(Assembly assembly) =>
        assembly.GetType("echo.Engines.ParakeetNpu.QnnRuntimeProfile", throwOnError: true)!
            .GetProperty("Whisper", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    private static (object Runtime, object Model) CreateWhisperDownloaders(
        Assembly assembly,
        ILoggerFactory loggerFactory,
        Func<HttpClient> httpFactory)
    {
        var runtimeType = assembly.GetType("echo.Engines.ParakeetNpu.QnnRuntimeDownloader", throwOnError: true)!;
        var modelType = assembly.GetType("echo.Engines.ParakeetNpu.Whisper.WhisperNpuModelDownloader", throwOnError: true)!;
        var runtime = Activator.CreateInstance(
            runtimeType,
            httpFactory(),
            CreateTypedLogger(loggerFactory, runtimeType),
            WhisperRuntimeProfile(assembly))!;
        var model = Activator.CreateInstance(modelType, httpFactory(), CreateTypedLogger(loggerFactory, modelType))!;
        return (runtime, model);
    }

    /// <summary>Whisper Turbo NPU engine inside the isolated worker (ORT 1.30 / QAIRT 2.50 from qnn-whisper\).</summary>
    internal static ITranscriptionEngine CreateWhisperNpuEngineForWorker(ILoggerFactory loggerFactory)
    {
        var assembly = LoadAssembly();
        var engineType = assembly.GetType("echo.Engines.ParakeetNpu.Whisper.WhisperNpuEngine", throwOnError: true)!;
        var (runtime, model) = CreateWhisperDownloaders(assembly, loggerFactory, () =>
        {
            var http = new HttpClient();
            ConfigureParakeetHttpClient(http);
            http.Timeout = Timeout.InfiniteTimeSpan;
            return http;
        });
        return (ITranscriptionEngine)Activator.CreateInstance(
            engineType,
            runtime,
            model,
            CreateTypedLogger(loggerFactory, engineType))!;
    }

    public static IWhisperNpuModelSupport CreateWhisperModelSupport(IServiceProvider services)
    {
        var assembly = LoadAssembly();
        var supportType = assembly.GetType("echo.Engines.ParakeetNpu.Whisper.WhisperNpuModelSupport", throwOnError: true)!;
        var factory = services.GetRequiredService<IHttpClientFactory>();
        var (runtime, model) = CreateWhisperDownloaders(
            assembly,
            services.GetRequiredService<ILoggerFactory>(),
            () => factory.CreateClient(WhisperNpuHttpClientName));
        return (IWhisperNpuModelSupport)Activator.CreateInstance(supportType, runtime, model)!;
    }

    internal static ITranscriptionEngine CreateInProcessEngine(IServiceProvider services) =>
        CreateEngineCore(services);

    public static IParakeetNpuModelSupport CreateModelSupport(IServiceProvider services)
    {
        var assembly = LoadAssembly();
        var supportType = assembly.GetType("echo.Engines.ParakeetNpu.ParakeetNpuModelSupport", throwOnError: true)!;
        var runtimeDownloader = CreateDownloader(
            services,
            assembly,
            "echo.Engines.ParakeetNpu.QnnRuntimeDownloader",
            QnnHttpClientName);
        var modelDownloader = CreateDownloader(
            services,
            assembly,
            "echo.Engines.ParakeetNpu.ParakeetModelDownloader",
            ModelHttpClientName);
        return (IParakeetNpuModelSupport)Activator.CreateInstance(
            supportType,
            runtimeDownloader,
            modelDownloader)!;
    }

    private static ITranscriptionEngine CreateRoutedEngine(IServiceProvider services)
    {
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var logger = CreateTypedLogger(loggerFactory, typeof(ParakeetWindowsEngine));
        return new ParakeetWindowsEngine(services, (ILogger)logger);
    }

    internal static ITranscriptionEngine CreateInProcessEngineForWorker(ILoggerFactory loggerFactory)
    {
        var assembly = LoadAssembly();
        var engineType = assembly.GetType("echo.Engines.ParakeetNpu.ParakeetNpuEngine", throwOnError: true)!;
        var runtimeDownloader = CreateDownloaderForWorker(
            loggerFactory,
            assembly,
            "echo.Engines.ParakeetNpu.QnnRuntimeDownloader");
        var modelDownloader = CreateDownloaderForWorker(
            loggerFactory,
            assembly,
            "echo.Engines.ParakeetNpu.ParakeetModelDownloader");
        var logger = CreateTypedLogger(loggerFactory, engineType);
        return (ITranscriptionEngine)Activator.CreateInstance(
            engineType,
            runtimeDownloader,
            modelDownloader,
            logger)!;
    }

    private static ITranscriptionEngine CreateEngineCore(IServiceProvider services)
    {
        var assembly = LoadAssembly();
        var engineType = assembly.GetType("echo.Engines.ParakeetNpu.ParakeetNpuEngine", throwOnError: true)!;
        var runtimeDownloader = CreateDownloader(
            services,
            assembly,
            "echo.Engines.ParakeetNpu.QnnRuntimeDownloader",
            QnnHttpClientName);
        var modelDownloader = CreateDownloader(
            services,
            assembly,
            "echo.Engines.ParakeetNpu.ParakeetModelDownloader",
            ModelHttpClientName);
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var logger = CreateTypedLogger(loggerFactory, engineType);
        return (ITranscriptionEngine)Activator.CreateInstance(
            engineType,
            runtimeDownloader,
            modelDownloader,
            logger)!;
    }

    private static object CreateDownloaderForWorker(
        ILoggerFactory loggerFactory,
        Assembly assembly,
        string typeName)
    {
        var downloaderType = assembly.GetType(typeName, throwOnError: true)!;
        var http = new HttpClient();
        ConfigureParakeetHttpClient(http);
        var logger = CreateTypedLogger(loggerFactory, downloaderType);
        return Activator.CreateInstance(downloaderType, http, logger)!;
    }

    private static object CreateDownloader(
        IServiceProvider services,
        Assembly assembly,
        string typeName,
        string httpClientName)
    {
        var downloaderType = assembly.GetType(typeName, throwOnError: true)!;
        var http = services.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var logger = CreateTypedLogger(loggerFactory, downloaderType);
        return Activator.CreateInstance(downloaderType, http, logger)!;
    }

    /// <summary>
    /// Activator requires exact parameter types; Parakeet ctors take ILogger&lt;T&gt;, not ILogger.
    /// </summary>
    internal static object CreateTypedLogger(ILoggerFactory loggerFactory, Type forType)
    {
        var createLogger = typeof(LoggerFactoryExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == nameof(LoggerFactoryExtensions.CreateLogger)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(ILoggerFactory));
        return createLogger.MakeGenericMethod(forType).Invoke(null, [loggerFactory])!;
    }

    private static void ConfigureParakeetHttpClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromHours(1);
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Echo/{UpdateEnvironment.CurrentVersion}");
    }

    private static Assembly LoadAssembly() => Assembly.Load(new AssemblyName(AssemblyName));
}
