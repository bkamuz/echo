using System.Reflection;
using echo.Abstractions.Engines;
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

        services.AddHttpClient(QnnHttpClientName);
        services.AddHttpClient(ModelHttpClientName);
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "parakeet_npu",
            Factory = CreateEngine,
        });
    }

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

    private static ITranscriptionEngine CreateEngine(IServiceProvider services)
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

    private static Assembly LoadAssembly() => Assembly.Load(new AssemblyName(AssemblyName));
}
