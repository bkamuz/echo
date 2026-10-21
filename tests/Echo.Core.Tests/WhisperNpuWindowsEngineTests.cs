using echo.Abstractions.Engines;
using echo.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.Core.Tests;

public sealed class WhisperNpuWindowsEngineTests
{
    private sealed class FakeWorker(bool failLoad, bool failTranscribe) : WhisperNpuWindowsEngine.IWhisperNpuWorker
    {
        public int Loads { get; private set; }

        public Task EnsureReadyAsync(EngineOptions options, CancellationToken cancellationToken)
        {
            Loads++;
            return failLoad ? Task.FromException(new InvalidOperationException("QNN context load failed")) : Task.CompletedTask;
        }

        public Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken) =>
            failTranscribe ? Task.FromException<string>(new IOException("worker crashed")) : Task.FromResult("npu text");

        public void Unload()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeSherpaWhisper : ITranscriptionEngine
    {
        public EngineOptions? Options { get; private set; }
        public string EngineId => "whisper";
        public string DisplayName => "sherpa whisper";
        public void Configure(EngineOptions options) => Options = options;
        public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default) =>
            Task.FromResult("cpu text");
        public void Unload()
        {
        }
    }

    private static (WhisperNpuWindowsEngine Engine, FakeSherpaWhisper Cpu) Create(FakeWorker worker)
    {
        var cpu = new FakeSherpaWhisper();
        var services = new ServiceCollection()
            .AddSingleton(new EngineRegistration { EngineId = "whisper", Factory = _ => cpu })
            .BuildServiceProvider();
        var engine = new WhisperNpuWindowsEngine(services, NullLogger.Instance, _ => worker, _ => Task.CompletedTask);
        engine.Configure(new EngineOptions { Engine = "whisper_npu", Device = "npu", Language = "ru" });
        return (engine, cpu);
    }

    [Fact]
    public async Task NpuWorks_UsesWorker()
    {
        var (engine, cpu) = Create(new FakeWorker(failLoad: false, failTranscribe: false));
        await engine.EnsureLoadedAsync();
        Assert.Equal("npu text", await engine.TranscribeAsync(new float[16000], 16000));
        Assert.False(engine.UsingCpuFallback);
        Assert.Null(cpu.Options);
    }

    [Fact]
    public async Task NpuLoadFails_FallsBackToSherpaTurboOnCpu()
    {
        var (engine, cpu) = Create(new FakeWorker(failLoad: true, failTranscribe: false));
        await engine.EnsureLoadedAsync();
        Assert.True(engine.UsingCpuFallback);
        Assert.Equal("cpu text", await engine.TranscribeAsync(new float[16000], 16000));
        Assert.Equal("whisper", cpu.Options!.Engine);
        Assert.Equal("large-v3-turbo", cpu.Options.WhisperModelSize);
        Assert.Equal("cpu", cpu.Options.Device);
        Assert.Equal("ru", cpu.Options.Language);
    }

    [Fact]
    public async Task NpuTranscribeCrashes_FallsBackAndStillReturnsText()
    {
        var worker = new FakeWorker(failLoad: false, failTranscribe: true);
        var (engine, _) = Create(worker);
        await engine.EnsureLoadedAsync();
        Assert.Equal("cpu text", await engine.TranscribeAsync(new float[16000], 16000));
        await engine.EnsureLoadedAsync();
        Assert.Equal(1, worker.Loads); // stays on CPU for the session
    }
}
