using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

public static class LinuxInjectionChain
{
    private static readonly ILinuxInjectionBackend[] Backends =
    [
        new AtSpiInjectionBackend(),
        new YdotoolInjectionBackend(),
        new X11InjectionBackend(),
        new WtypeInjectionBackend(),
        new ClipboardFallbackBackend(),
    ];

    public static bool HasAutoInjectionBackend =>
        Backends.Any(backend => backend is not ClipboardFallbackBackend && backend.IsAvailable);

    public static TextInjectionResult Inject(
        string text,
        string method,
        int typeDelayMs,
        int preInjectDelayMs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return TextInjectionResult.AutoPasted;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (preInjectDelayMs > 0)
        {
            Thread.Sleep(preInjectDelayMs);
        }

        var normalizedMethod = NormalizeMethod(method);

        foreach (var backend in Backends)
        {
            if (!backend.IsAvailable)
            {
                continue;
            }

            var result = backend.TryInject(text, normalizedMethod, typeDelayMs, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        return TextInjectionResult.Failed("No injection backend available.");
    }

    public static void ResetProbes()
    {
        WtypeInjectionBackend.ResetProbe();
        YdotoolInjectionBackend.ResetProbe();
        LinuxAtSpiInserter.ResetProbe();
    }

    private static string NormalizeMethod(string method) =>
        method switch
        {
            "clipboard" => "clipboard",
            "type" => "auto",
            _ => "auto",
        };
}
