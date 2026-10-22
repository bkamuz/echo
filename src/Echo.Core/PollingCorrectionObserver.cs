using echo.Abstractions.Platform;
using Microsoft.Extensions.Logging;

namespace echo.Core;

/// <summary>Snapshot of the focused text field as read by a platform accessibility API.</summary>
/// <param name="Id">Stable identity of the field, used to detect that focus moved elsewhere.</param>
/// <param name="App">Process / application name (matched against excluded apps).</param>
/// <param name="IsPassword">True for password fields; these are never watched.</param>
/// <param name="Text">Full field text, or null when the field is not readable.</param>
public sealed record FocusedFieldSnapshot(string Id, string App, bool IsPassword, string? Text);

/// <summary>
/// Platform-independent correction learning loop: after a paste, takes a baseline of the focused field,
/// tracks the inserted span while the user edits it, and turns the final edit into dictionary suggestions.
/// Platforms only provide <see cref="ReadFocused"/> (Linux: AT-SPI2, macOS: AXUIElement).
/// </summary>
public abstract class PollingCorrectionObserver : ICorrectionObserver
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan WarmupWindow = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan WarmupStep = TimeSpan.FromMilliseconds(250);

    private readonly DictionaryStore _store;
    private readonly object _gate = new();
    private readonly HashSet<string> _unreadableLogged = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _current;

    protected PollingCorrectionObserver(DictionaryStore store, ILogger logger)
    {
        _store = store;
        Logger = logger;
    }

    protected ILogger Logger { get; }

    /// <summary>Whether the platform API can be used at all (checked once per dictation).</summary>
    protected virtual bool IsAvailable() => true;

    /// <summary>Reads the currently focused editable field, or null if none / unreadable.</summary>
    protected abstract FocusedFieldSnapshot? ReadFocused();

    public void Observe(string injectedText)
    {
        var settings = _store.Snapshot().Learning;
        if (!settings.Enabled || CorrectionExtractor.Tokenize(injectedText).Count == 0)
        {
            return;
        }

        CancellationTokenSource cts;
        lock (_gate)
        {
            _current?.Cancel();
            // Ended by the next dictation or the session hard cap; idle timeout is tracked in the loop.
            _current = cts = new CancellationTokenSource(CorrectionSession.HardCap);
        }

        var thread = new Thread(() => Run(injectedText.Trim(), settings, cts.Token))
        {
            IsBackground = true,
            Name = "Echo correction observer",
        };
        thread.Start();
    }

    private void Run(string injected, LearningSettings settings, CancellationToken ct)
    {
        try
        {
            if (!IsAvailable() || ct.WaitHandle.WaitOne(400))
            {
                return;
            }

            var self = Environment.ProcessPath is { } p ? Path.GetFileNameWithoutExtension(p) : "Echo.App";
            FocusedFieldSnapshot? field = null;
            InjectedSpanTracker? tracker = null;
            var deadline = DateTime.UtcNow + WarmupWindow;
            while (true)
            {
                field = ReadFocused();
                if (field is not null)
                {
                    if (field.IsPassword || field.App.Equals(self, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    if (settings.IsExcluded(field.App))
                    {
                        Logger.LogDebug("Correction learning: {App} excluded", field.App);
                        return;
                    }

                    tracker = field.Text is null ? null : InjectedSpanTracker.Create(Normalize(field.Text), injected);
                    if (tracker is not null)
                    {
                        break;
                    }
                }

                if (DateTime.UtcNow >= deadline || ct.WaitHandle.WaitOne(WarmupStep))
                {
                    break;
                }
            }

            if (field is null || tracker is null)
            {
                var app = field?.App ?? "?";
                lock (_unreadableLogged)
                {
                    if (_unreadableLogged.Add(app))
                    {
                        Logger.LogInformation(
                            "Correction learning unavailable in {App}: accessibility API did not expose the inserted text", app);
                    }
                }

                return;
            }

            Logger.LogDebug("Correction learning: watching {App}", field.App);
            var session = new CorrectionSession(TimeSpan.FromSeconds(settings.ClampedSeconds));
            while (!ct.WaitHandle.WaitOne(PollInterval) && !session.IsIdle)
            {
                var now = ReadFocused();
                if (now is null || now.Id != field.Id)
                {
                    Logger.LogDebug("Correction learning: focus left the field in {App}", field.App);
                    break;
                }

                // A transiently unreadable span (edit outside it, mid-typing state) keeps the last good one.
                session.Observe(now.Text is null ? null : tracker.CurrentSpan(Normalize(now.Text)));
            }

            var lastSpan = session.LastSpan;

            if (lastSpan is null || lastSpan.Trim() == injected)
            {
                Logger.LogDebug("Correction learning: no edit seen in {App}", field.App);
                return;
            }

            var added = _store.AddSuggestions(CorrectionExtractor.Extract(injected, lastSpan), field.App);
            if (added > 0)
            {
                Logger.LogInformation("Correction learning: {Count} suggestion(s) from {App}", added, field.App);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Correction learning failed");
        }
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');
}
