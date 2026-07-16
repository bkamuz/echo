using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

public static class LinuxInjectionChain
{
    private static readonly ILinuxInjectionBackend[] AllBackends =
    [
        new AtSpiInjectionBackend(),
        new YdotoolInjectionBackend(),
        new X11InjectionBackend(),
        new WtypeInjectionBackend(),
        new ClipboardFallbackBackend(),
    ];

    private static ILinuxInjectionBackend? _cachedPrimary;
    private static readonly object CacheGate = new();

    public static bool HasAutoInjectionBackend =>
        AllBackends.Any(backend => backend is not ClipboardFallbackBackend && backend.IsAvailable);

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
        var clipboard = AllBackends.OfType<ClipboardFallbackBackend>().First();

        foreach (var backend in ResolveAttemptOrder())
        {
            if (!backend.IsAvailable)
            {
                continue;
            }

            var result = backend.TryInject(text, normalizedMethod, typeDelayMs, cancellationToken);
            if (result is not null)
            {
                if (backend is not ClipboardFallbackBackend)
                {
                    CachePrimary(backend);
                }

                return result;
            }
        }

        if (clipboard.IsAvailable)
        {
            var fallback = clipboard.TryInject(text, normalizedMethod, typeDelayMs, cancellationToken);
            if (fallback is not null)
            {
                return fallback;
            }
        }

        return TextInjectionResult.Failed("No injection backend available.");
    }

    public static void ResetProbes()
    {
        lock (CacheGate)
        {
            _cachedPrimary = null;
        }

        WtypeInjectionBackend.ResetProbe();
        YdotoolInjectionBackend.ResetProbe();
        LinuxAtSpiInserter.ResetProbe();
    }

    private static IEnumerable<ILinuxInjectionBackend> ResolveAttemptOrder()
    {
        lock (CacheGate)
        {
            if (_cachedPrimary is not null && _cachedPrimary.IsAvailable)
            {
                yield return _cachedPrimary;
                yield break;
            }
        }

        // Prefer session-appropriate primary, then remaining auto backends (clipboard last, handled separately).
        foreach (var backend in PreferredPrimaries().Concat(AllBackends))
        {
            if (backend is ClipboardFallbackBackend)
            {
                continue;
            }

            yield return backend;
        }
    }

    private static IEnumerable<ILinuxInjectionBackend> PreferredPrimaries()
    {
        if (LinuxDependencyCatalog.UsesGnomeWaylandYdotool)
        {
            yield return AllBackends.OfType<YdotoolInjectionBackend>().First();
        }
    }

    private static void CachePrimary(ILinuxInjectionBackend backend)
    {
        lock (CacheGate)
        {
            _cachedPrimary = backend;
        }
    }

    private static string NormalizeMethod(string method) =>
        method switch
        {
            "clipboard" => "clipboard",
            "type" => "auto",
            _ => "auto",
        };
}
