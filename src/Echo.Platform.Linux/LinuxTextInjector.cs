using echo.Abstractions.Platform;
using echo.Platform.Linux.Injection;

namespace echo.Platform.Linux;

public sealed class LinuxTextInjector : ITextInjector
{
    private static readonly SemaphoreSlim InjectGate = new(1, 1);
    private const int DefaultPreInjectDelayMs = 120;

    public async Task<TextInjectionResult> InjectAsync(
        string text,
        string method,
        int typeDelayMs = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return TextInjectionResult.AutoPasted;
        }

        await InjectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () =>
                {
                    var attempt = LinuxInjectionChain.Inject(
                        text,
                        method,
                        typeDelayMs,
                        DefaultPreInjectDelayMs,
                        cancellationToken);
                    return attempt.Result;
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            InjectGate.Release();
        }
    }

    public async Task<LinuxInjectionAttempt> InjectWithDetailsAsync(
        string text,
        string method,
        int typeDelayMs = 0,
        CancellationToken cancellationToken = default)
    {
        await InjectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => LinuxInjectionChain.Inject(
                    text,
                    method,
                    typeDelayMs,
                    DefaultPreInjectDelayMs,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            InjectGate.Release();
        }
    }
}
