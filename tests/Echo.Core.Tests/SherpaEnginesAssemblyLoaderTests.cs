using echo.Abstractions.Engines;
using echo.Core.DependencyInjection;
using echo.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Core.Tests;

public class SherpaEnginesAssemblyLoaderTests
{
    [Fact]
    public void RegisterEngines_ExposesEngineIdsWithoutEagerConstruction()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.UseEcho();
        SherpaEnginesAssemblyLoader.RegisterEngines(services);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var registry = provider.GetRequiredService<ITranscriptionEngineRegistry>();

        Assert.Contains("gigaam", registry.RegisteredEngineIds);
        Assert.Contains("omnilingual", registry.RegisteredEngineIds);
    }

    [Fact]
    public void RegisterEngines_CreatesSherpaEngineOnFirstGetRequired()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.UseEcho();
        SherpaEnginesAssemblyLoader.RegisterEngines(services);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var registry = provider.GetRequiredService<ITranscriptionEngineRegistry>();

        var engine = registry.GetRequired("gigaam");

        Assert.Equal("gigaam", engine.EngineId);
    }
}
