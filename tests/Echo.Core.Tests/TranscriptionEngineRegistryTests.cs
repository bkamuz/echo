using echo.Abstractions.Engines;
using echo.Core;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Core.Tests;

public class TranscriptionEngineRegistryTests
{
    [Fact]
    public void GetRequired_LazilyCreatesEngineOnlyOnFirstUse()
    {
        var createCount = 0;
        var services = new ServiceCollection();
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "test",
            Factory = _ =>
            {
                createCount++;
                return new StubEngine("test");
            },
        });
        var provider = services.BuildServiceProvider();
        var registry = new TranscriptionEngineRegistry(
            provider,
            [provider.GetRequiredService<EngineRegistration>()]);

        Assert.Equal(["test"], registry.RegisteredEngineIds);
        Assert.Equal(0, createCount);

        var first = registry.GetRequired("test");
        var second = registry.GetRequired("test");

        Assert.Equal(1, createCount);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetRequired_ThrowsForUnknownEngine()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var registry = new TranscriptionEngineRegistry(provider, []);

        Assert.Throws<InvalidOperationException>(() => registry.GetRequired("missing"));
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
