using System.Diagnostics;
using System.Runtime.InteropServices;
using echo.Abstractions.Platform;
using echo.Core;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

/// <summary>
/// After a paste, polls the focused control through UI Automation (TextPattern, then Value) for a
/// configurable window and turns edits of the inserted span into dictionary suggestions.
/// Text outside the inserted span is only kept in memory as an anchor and never stored.
/// </summary>
public sealed class UiaCorrectionObserver : ICorrectionObserver
{
    private const int UIA_TextPatternId = 10014;
    private const int UIA_ProcessIdPropertyId = 30002;
    private const int UIA_IsPasswordPropertyId = 30019;
    private const int UIA_ValueValuePropertyId = 30045;
    private const int MaxTextLength = 20000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1000);

    private readonly DictionaryStore _store;
    private readonly ILogger<UiaCorrectionObserver> _logger;
    private readonly HashSet<string> _unreadableLogged = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private CancellationTokenSource? _current;

    public UiaCorrectionObserver(DictionaryStore store, ILogger<UiaCorrectionObserver> logger)
    {
        _store = store;
        _logger = logger;
    }

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
            // A new dictation ends the previous watch (its last good span is still evaluated).
            _current?.Cancel();
            _current = cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.ClampedSeconds));
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
            var automation = (IUIAutomation)new CUIAutomation();
            // Let the target app finish handling Ctrl+V before taking the baseline.
            if (ct.WaitHandle.WaitOne(400))
            {
                return;
            }

            var element = automation.GetFocusedElement();
            if (element is null)
            {
                return;
            }

            var app = ProcessName(element);
            if (app is null || app.Equals(Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (settings.IsExcluded(app))
            {
                _logger.LogDebug("Correction learning: {App} excluded", app);
                return;
            }

            if (IsPassword(element))
            {
                return;
            }

            var baseline = ReadText(element);
            var tracker = baseline is null ? null : InjectedSpanTracker.Create(baseline, injected);
            if (tracker is null)
            {
                lock (_unreadableLogged)
                {
                    if (_unreadableLogged.Add(app))
                    {
                        _logger.LogInformation(
                            "Correction learning unavailable in {App}: UI Automation cannot read the inserted text",
                            app);
                    }
                }

                return;
            }

            string? lastSpan = null;
            while (!ct.WaitHandle.WaitOne(PollInterval))
            {
                var focused = automation.GetFocusedElement();
                if (focused is null || automation.CompareElements(focused, element) == 0)
                {
                    break; // user moved on; evaluate what we saw last
                }

                var text = ReadText(element);
                var span = text is null ? null : tracker.CurrentSpan(text);
                if (span is null)
                {
                    break; // field sent/cleared or edited outside the span
                }

                lastSpan = span;
            }

            if (lastSpan is null || lastSpan.Trim() == injected)
            {
                return;
            }

            var candidates = CorrectionExtractor.Extract(injected, lastSpan);
            var added = _store.AddSuggestions(candidates, app);
            if (added > 0)
            {
                _logger.LogInformation("Correction learning: {Count} suggestion(s) from {App}", added, app);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Correction learning stopped: UI Automation error");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Correction learning failed");
        }
    }

    private static string? ProcessName(IUIAutomationElement element)
    {
        try
        {
            var pid = Convert.ToInt32(element.GetCurrentPropertyValue(UIA_ProcessIdPropertyId));
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPassword(IUIAutomationElement element)
    {
        try
        {
            return element.GetCurrentPropertyValue(UIA_IsPasswordPropertyId) is true;
        }
        catch (COMException)
        {
            return true;
        }
    }

    private static string? ReadText(IUIAutomationElement element)
    {
        try
        {
            if (element.GetCurrentPattern(UIA_TextPatternId) is IUIAutomationTextPattern tp)
            {
                var text = tp.DocumentRange?.GetText(MaxTextLength);
                if (!string.IsNullOrEmpty(text))
                {
                    return Normalize(text);
                }
            }
        }
        catch (COMException)
        {
        }

        try
        {
            return element.GetCurrentPropertyValue(UIA_ValueValuePropertyId) is string { Length: > 0 and <= MaxTextLength } v
                ? Normalize(v)
                : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    // ---- Minimal UI Automation COM interop (only the vtable slots we call are typed) ----

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    private class CUIAutomation;

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        int CompareElements(IUIAutomationElement el1, IUIAutomationElement el2);
        void CompareRuntimeIds();
        void GetRootElement();
        void ElementFromHandle();
        void ElementFromPoint();
        IUIAutomationElement GetFocusedElement();
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll();
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValue(int propertyId);
        void GetCurrentPropertyValueEx();
        void GetCachedPropertyValue();
        void GetCachedPropertyValueEx();
        void GetCurrentPatternAs();
        void GetCachedPatternAs();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object? GetCurrentPattern(int patternId);
    }

    [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        void RangeFromPoint();
        void RangeFromChild();
        void GetSelection();
        void GetVisibleRanges();
        IUIAutomationTextRange? DocumentRange { get; }
    }

    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        void Clone();
        void Compare();
        void CompareEndpoints();
        void ExpandToEnclosingUnit();
        void FindAttribute();
        void FindText();
        void GetAttributeValue();
        void GetBoundingRectangles();
        void GetEnclosingElement();
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetText(int maxLength);
    }
}
