using echo.Abstractions.Engines;

namespace echo.Core;

public sealed class TranscriptionEngineRegistry : ITranscriptionEngineRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Func<IServiceProvider, ITranscriptionEngine>> _factories;
    private readonly Dictionary<string, Lazy<ITranscriptionEngine>> _cache = new(StringComparer.Ordinal);

    public TranscriptionEngineRegistry(
        IServiceProvider serviceProvider,
        IEnumerable<EngineRegistration> registrations)
    {
        _serviceProvider = serviceProvider;
        _factories = registrations.ToDictionary(
            registration => registration.EngineId,
            registration => registration.Factory,
            StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> RegisteredEngineIds => _factories.Keys.ToList();

    public bool IsRegistered(string engineId) => _factories.ContainsKey(engineId);

    public ITranscriptionEngine GetRequired(string engineId)
    {
        if (!_factories.TryGetValue(engineId, out var factory))
        {
            throw new InvalidOperationException($"Engine '{engineId}' is not registered.");
        }

        if (!_cache.TryGetValue(engineId, out var lazy))
        {
            lazy = new Lazy<ITranscriptionEngine>(() => factory(_serviceProvider));
            _cache[engineId] = lazy;
        }

        return lazy.Value;
    }
}
