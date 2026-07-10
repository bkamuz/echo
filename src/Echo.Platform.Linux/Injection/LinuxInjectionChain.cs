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

    public static IReadOnlyList<(string Name, bool Available)> ProbeBackends()
    {
        return Backends
            .Select(backend => (backend.Name, backend.IsAvailable))
            .ToList();
    }

    public static bool HasAutoInjectionBackend =>
        Backends.Any(backend => backend is not ClipboardFallbackBackend && backend.IsAvailable);

    public static LinuxInjectionAttempt Inject(
        string text,
        string method,
        int typeDelayMs,
        int preInjectDelayMs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new LinuxInjectionAttempt(TextInjectionResult.AutoPasted, "none");
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
            if (result is null)
            {
                continue;
            }

            return new LinuxInjectionAttempt(result, backend.Name);
        }

        return new LinuxInjectionAttempt(
            TextInjectionResult.Failed("No injection backend available."),
            "none");
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
