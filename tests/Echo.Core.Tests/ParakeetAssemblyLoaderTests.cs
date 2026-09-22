using echo.Abstractions.Engines;
using echo.Engines.ParakeetNpu;
using echo.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.Core.Tests;

public class ParakeetAssemblyLoaderTests
{
    [Fact]
    public void CreateTypedLogger_ReturnsGenericLoggerMatchingConstructor()
    {
        var logger = ParakeetAssemblyLoader.CreateTypedLogger(
            NullLoggerFactory.Instance,
            typeof(QnnRuntimeDownloader));

        Assert.IsAssignableFrom<ILogger<QnnRuntimeDownloader>>(logger);

        var instance = Activator.CreateInstance(
            typeof(QnnRuntimeDownloader),
            new HttpClient(),
            logger);

        Assert.IsType<QnnRuntimeDownloader>(instance);
    }

    [Fact]
    public void CreateTypedLogger_BindsParakeetModelDownloaderConstructor()
    {
        var logger = ParakeetAssemblyLoader.CreateTypedLogger(
            NullLoggerFactory.Instance,
            typeof(ParakeetModelDownloader));

        Assert.IsAssignableFrom<ILogger<ParakeetModelDownloader>>(logger);

        var instance = Activator.CreateInstance(
            typeof(ParakeetModelDownloader),
            new HttpClient(),
            logger);

        Assert.IsType<ParakeetModelDownloader>(instance);
    }

    [Fact]
    public void CreateTypedLogger_NonGenericLoggerFailsParakeetConstructors()
    {
        var nonGenericLogger = NullLoggerFactory.Instance.CreateLogger(typeof(QnnRuntimeDownloader).FullName!);

        Assert.Throws<MissingMethodException>(() =>
            Activator.CreateInstance(
                typeof(QnnRuntimeDownloader),
                new HttpClient(),
                nonGenericLogger));
    }

    [Fact]
    public void RegisterEngine_ExposesParakeetWithoutEagerConstruction()
    {
        if (!ParakeetAssemblyLoader.IsSupported)
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient(ParakeetAssemblyLoader.QnnHttpClientName);
        services.AddHttpClient(ParakeetAssemblyLoader.ModelHttpClientName);
        ParakeetAssemblyLoader.RegisterEngine(services);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var registry = provider.GetRequiredService<ITranscriptionEngineRegistry>();

        Assert.Contains("parakeet_npu", registry.RegisteredEngineIds);
    }
}
