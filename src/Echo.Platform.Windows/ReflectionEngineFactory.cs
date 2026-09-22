using System.Reflection;
using echo.Abstractions.Engines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

internal static class ReflectionEngineFactory
{
    public static ITranscriptionEngine CreateEngine(
        IServiceProvider services,
        Assembly assembly,
        string typeName)
    {
        var engineType = assembly.GetType(typeName, throwOnError: true)!;
        var logger = CreateTypedLogger(services, engineType);
        return (ITranscriptionEngine)Activator.CreateInstance(engineType, logger)!;
    }

    public static object CreateTypedLogger(IServiceProvider services, Type forType)
    {
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var createLogger = typeof(LoggerFactoryExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == nameof(LoggerFactoryExtensions.CreateLogger)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(ILoggerFactory));
        return createLogger.MakeGenericMethod(forType).Invoke(null, [loggerFactory])!;
    }
}
