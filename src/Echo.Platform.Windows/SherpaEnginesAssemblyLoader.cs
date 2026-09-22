using System.Reflection;
using echo.Abstractions.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Platform.Windows;

/// <summary>
/// Loads Echo.Engines (Sherpa/GigaAM) on first engine use via reflection so SherpaOnnx
/// natives are not pulled into the Windows startup assembly graph.
/// </summary>
internal static class SherpaEnginesAssemblyLoader
{
    private const string AssemblyName = "Echo.Engines";

    public static void RegisterEngines(IServiceCollection services)
    {
        RegisterEngine(services, "gigaam", "echo.Engines.GigaAm.GigaAmEngine");
        RegisterEngine(services, "omnilingual", "echo.Engines.Omnilingual.OmnilingualEngine");
        TryRegisterEngine(services, "whisper", "echo.Engines.Whisper.WhisperEngine");
    }

    private static void RegisterEngine(IServiceCollection services, string engineId, string typeName)
    {
        services.AddSingleton(new EngineRegistration
        {
            EngineId = engineId,
            Factory = sp => SherpaWorkerPolicy.ShouldIsolate()
                ? CreateOutOfProcessEngine(sp, engineId)
                : CreateEngine(sp, typeName),
        });
    }

    private static void TryRegisterEngine(IServiceCollection services, string engineId, string typeName)
    {
        services.AddSingleton(new EngineRegistration
        {
            EngineId = engineId,
            Factory = sp =>
            {
                if (SherpaWorkerPolicy.ShouldIsolate())
                {
                    return CreateOutOfProcessEngine(sp, engineId);
                }

                var assembly = LoadAssembly();
                if (assembly.GetType(typeName, throwOnError: false) is null)
                {
                    throw new InvalidOperationException($"Engine '{engineId}' is not available in this Echo build.");
                }

                return CreateEngine(sp, typeName);
            },
        });
    }

    private static ITranscriptionEngine CreateEngine(IServiceProvider services, string typeName)
    {
        var assembly = LoadAssembly();
        return ReflectionEngineFactory.CreateEngine(services, assembly, typeName);
    }

    private static ITranscriptionEngine CreateOutOfProcessEngine(IServiceProvider services, string engineId)
    {
        var logger = ReflectionEngineFactory.CreateTypedLogger(
            services,
            typeof(SherpaOutOfProcessEngine));
        return new SherpaOutOfProcessEngine(engineId, (Microsoft.Extensions.Logging.ILogger)logger);
    }

    private static Assembly LoadAssembly() => Assembly.Load(new AssemblyName(AssemblyName));

    internal static Assembly LoadAssemblyForWorker() => LoadAssembly();
}
