using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using echo.Abstractions.Engines;

namespace echo.Platform.Windows;

internal enum SherpaWorkerCommand : byte
{
    Configure = 1,
    EnsureLoaded = 2,
    Transcribe = 3,
    Unload = 4,
    Ping = 5,
}

internal enum SherpaWorkerStatus : byte
{
    Ok = 0,
    Error = 1,
}

internal sealed record SherpaWorkerConfigurePayload(string EngineId, EngineOptions Options);

internal static class SherpaWorkerProtocol
{
    private const byte ProtocolVersion = 1;

    public static async Task WriteRequestAsync(
        Stream stream,
        SherpaWorkerCommand command,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var commandHeader = new byte[] { ProtocolVersion, (byte)command };
        await stream.WriteAsync(commandHeader, cancellationToken).ConfigureAwait(false);
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, payload.Length);
        await stream.WriteAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<(SherpaWorkerStatus Status, byte[] Payload)> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, 6, cancellationToken).ConfigureAwait(false);
        if (header[0] != ProtocolVersion)
        {
            throw new InvalidOperationException($"Unsupported Sherpa worker protocol version {header[0]}.");
        }

        var status = (SherpaWorkerStatus)header[1];
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(2, 4));
        if (length < 0)
        {
            throw new InvalidOperationException("Sherpa worker returned a negative payload length.");
        }

        var payload = length == 0 ? [] : await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
        return (status, payload);
    }

    public static byte[] SerializeConfigure(string engineId, EngineOptions options)
    {
        var payload = new SherpaWorkerConfigurePayload(engineId, options);
        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    public static SherpaWorkerConfigurePayload DeserializeConfigure(ReadOnlySpan<byte> payload) =>
        JsonSerializer.Deserialize<SherpaWorkerConfigurePayload>(payload)
        ?? throw new InvalidOperationException("Sherpa worker configure payload was empty.");

    public static byte[] SerializeTranscribe(int sampleRate, float[] samples)
    {
        var payload = new byte[8 + samples.Length * sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), samples.Length);
        Buffer.BlockCopy(samples, 0, payload, 8, samples.Length * sizeof(float));
        return payload;
    }

    public static (int SampleRate, float[] Samples) DeserializeTranscribe(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            throw new InvalidOperationException("Sherpa worker transcribe payload was truncated.");
        }

        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(payload);
        var sampleCount = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        if (sampleCount < 0)
        {
            throw new InvalidOperationException("Sherpa worker transcribe sample count was negative.");
        }

        var expectedBytes = 8 + sampleCount * sizeof(float);
        if (payload.Length < expectedBytes)
        {
            throw new InvalidOperationException("Sherpa worker transcribe audio payload was truncated.");
        }

        var samples = new float[sampleCount];
        Buffer.BlockCopy(payload.ToArray(), 8, samples, 0, sampleCount * sizeof(float));
        return (sampleRate, samples);
    }

    public static byte[] SerializeText(string text) => Encoding.UTF8.GetBytes(text);

    public static string DeserializeText(ReadOnlySpan<byte> payload) => Encoding.UTF8.GetString(payload);

    public static byte[] SerializeError(string message) => Encoding.UTF8.GetBytes(message);

    public static string DeserializeError(ReadOnlySpan<byte> payload) =>
        payload.IsEmpty ? "Sherpa worker failed." : Encoding.UTF8.GetString(payload);

    public static async Task WriteResponseAsync(
        Stream stream,
        SherpaWorkerStatus status,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var header = new byte[6];
        header[0] = ProtocolVersion;
        header[1] = (byte)status;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2, 4), payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Sherpa worker pipe closed unexpectedly.");
            }

            offset += read;
        }

        return buffer;
    }
}
