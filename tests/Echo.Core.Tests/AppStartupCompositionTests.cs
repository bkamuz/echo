using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using echo.Core;
using echo.Core.DependencyInjection;
using echo.Engines.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Core.Tests;

public class AppStartupCompositionTests
{
    [Fact]
    public void HostBuild_ResolvesEngineRegistryWithoutCreatingEngines()
    {
        var createCount = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.UseEcho();
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "gigaam",
            Factory = _ =>
            {
                createCount++;
                return new StubEngine("gigaam");
            },
        });
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "omnilingual",
            Factory = _ => new StubEngine("omnilingual"),
        });

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var registry = provider.GetRequiredService<ITranscriptionEngineRegistry>();
        var transcription = provider.GetRequiredService<TranscriptionService>();

        Assert.Equal(2, registry.RegisteredEngineIds.Count);
        Assert.Equal(0, createCount);

        _ = EngineDisplayNames.ForConfig(new AppConfig { Engine = "gigaam" });
        Assert.Equal(0, createCount);

        _ = transcription.Resolve(new AppConfig { Engine = "gigaam" });
        Assert.Equal(1, createCount);
    }

    [Fact]
    public void UseEchoEngines_RegistersDistinctEngineIdsForRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.UseEcho();
        services.UseEchoEngines();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var registry = provider.GetRequiredService<ITranscriptionEngineRegistry>();

        Assert.Contains("gigaam", registry.RegisteredEngineIds);
        Assert.Contains("omnilingual", registry.RegisteredEngineIds);
    }

    private sealed class StubEngine(string engineId) : ITranscriptionEngine
    {
        public string EngineId => engineId;

        public string DisplayName => engineId;

        public void Configure(EngineOptions options)
        {
        }

        public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public void Unload()
        {
        }
    }
}
