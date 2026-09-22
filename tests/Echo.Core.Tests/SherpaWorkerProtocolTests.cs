using echo.Abstractions.Engines;
using echo.Platform.Windows;

namespace echo.Core.Tests;

public class SherpaWorkerProtocolTests
{
    [Fact]
    public async Task ConfigureAndTranscribePayloads_RoundTrip()
    {
        var options = new EngineOptions
        {
            Engine = "gigaam",
            GigaAmModelSize = "e2e",
            Device = "cpu",
            SampleRate = 16000,
        };

        var configureBytes = SherpaWorkerProtocol.SerializeConfigure("gigaam", options);
        var configure = SherpaWorkerProtocol.DeserializeConfigure(configureBytes);
        Assert.Equal("gigaam", configure.EngineId);
        Assert.Equal("e2e", configure.Options.GigaAmModelSize);

        var samples = new float[] { 0.1f, -0.2f, 0.3f };
        var transcribeBytes = SherpaWorkerProtocol.SerializeTranscribe(16000, samples);
        var (sampleRate, decoded) = SherpaWorkerProtocol.DeserializeTranscribe(transcribeBytes);
        Assert.Equal(16000, sampleRate);
        Assert.Equal(samples, decoded);
    }

    [Fact]
    public async Task WriteResponse_ReadResponse_RoundTrip()
    {
        await using var stream = new MemoryStream();
        var payload = SherpaWorkerProtocol.SerializeText("hello");
        await SherpaWorkerProtocol.WriteResponseAsync(
            stream,
            SherpaWorkerStatus.Ok,
            payload,
            CancellationToken.None);

        stream.Position = 0;
        var (status, responsePayload) = await SherpaWorkerProtocol.ReadResponseAsync(stream, CancellationToken.None);
        Assert.Equal(SherpaWorkerStatus.Ok, status);
        Assert.Equal("hello", SherpaWorkerProtocol.DeserializeText(responsePayload));
    }
}
