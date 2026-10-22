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
    private const int UIA_NamePropertyId = 30005;
    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_ClassNamePropertyId = 30012;
    private const int UIA_ValuePatternId = 10002;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan WarmupWindow = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan WarmupStep = TimeSpan.FromMilliseconds(250);

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
            var automation = (IUIAutomation)new CUIAutomation();
            // Let the target app finish handling Ctrl+V before taking the baseline.
            if (ct.WaitHandle.WaitOne(400))
            {
                return;
            }

            // Chromium/Electron (Cursor, VS Code, browsers) build their accessibility tree lazily: the first
            // UIA query only switches it on and returns the bare render-widget pane, and the tree can lag
            // the paste. Re-query focus until the inserted text shows up or the warm-up window ends.
            IUIAutomationElement? element = null;
            InjectedSpanTracker? tracker = null;
            string? app = null;
            FieldInfo info = default;
            var warmupDeadline = DateTime.UtcNow + WarmupWindow;
            var attempts = 0;
            while (true)
            {
                attempts++;
                element = automation.GetFocusedElement();
                if (element is not null)
                {
                    app = ProcessName(element);
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

                    info = Describe(element);
                    var baseline = info.IsPlaceholder ? null : ReadText(element);
                    tracker = baseline is null ? null : InjectedSpanTracker.Create(baseline, injected);
                    if (tracker is not null)
                    {
                        break;
                    }
                }

                if (DateTime.UtcNow >= warmupDeadline || ct.WaitHandle.WaitOne(WarmupStep))
                {
                    break;
                }
            }

            if (element is null || app is null || tracker is null)
            {
                if (app is not null)
                {
                    LogUnreadable(app, info, attempts);
                }

                return;
            }

            _logger.LogDebug(
                "Correction learning: watching {App} ({Type}/{Class}) after {Attempts} attempt(s)",
                app, info.ControlType, info.ClassName, attempts);

            var session = new CorrectionSession(TimeSpan.FromSeconds(settings.ClampedSeconds));
            while (!ct.WaitHandle.WaitOne(PollInterval) && !session.IsIdle)
            {
                var focused = automation.GetFocusedElement();
                if (focused is null || automation.CompareElements(focused, element) == 0)
                {
                    _logger.LogDebug("Correction learning: focus left the field in {App}", app);
                    break; // user moved on; evaluate what we saw last
                }

                var text = ReadText(element);
                // A transiently unreadable span (edit outside it, mid-typing state) keeps the last good one.
                session.Observe(text is null ? null : tracker.CurrentSpan(text));
            }

            var lastSpan = session.LastSpan;

            if (lastSpan is null || lastSpan.Trim() == injected)
            {
                _logger.LogDebug("Correction learning: no edit seen in {App}", app);
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

    private readonly record struct FieldInfo(
        string ControlType, string ClassName, bool HasText, bool HasValue, int ValueLength, bool IsPlaceholder, bool IsMonaco);

    private static FieldInfo Describe(IUIAutomationElement element)
    {
        string Str(int id)
        {
            try
            {
                return element.GetCurrentPropertyValue(id)?.ToString() ?? "";
            }
            catch (COMException)
            {
                return "";
            }
        }

        var type = Str(UIA_ControlTypePropertyId);
        var cls = Str(UIA_ClassNamePropertyId);
        var name = Str(UIA_NamePropertyId);
        var value = Str(UIA_ValueValuePropertyId);
        bool hasText = false, hasValue = false;
        string? docText = null;
        try
        {
            docText = DocumentText(element);
            hasText = docText is not null || element.GetCurrentPattern(UIA_TextPatternId) is not null;
            hasValue = element.GetCurrentPattern(UIA_ValuePatternId) is not null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
        }

        var monaco = cls.Contains("inputarea", StringComparison.OrdinalIgnoreCase);
        // Monaco without screen-reader mode keeps the real buffer out of the DOM: Value is empty and the
        // TextPattern only returns its accessible name ("The editor is not accessible at this time...").
        var placeholder = value.Length == 0 && !string.IsNullOrEmpty(docText) && docText.Trim() == name.Trim();
        return new FieldInfo(ControlTypeName(type), cls, hasText, hasValue, value.Length, placeholder, monaco);
    }

    private static string ControlTypeName(string id) => id switch
    {
        "50004" => "Edit",
        "50030" => "Document",
        "50033" => "Pane",
        "50020" => "Text",
        "50032" => "Window",
        "" => "?",
        _ => id,
    };

    private void LogUnreadable(string app, FieldInfo info, int attempts)
    {
        lock (_unreadableLogged)
        {
            if (!_unreadableLogged.Add($"{app}|{info.ClassName}|{info.IsPlaceholder}"))
            {
                return;
            }
        }

        if (info.IsMonaco && info.IsPlaceholder)
        {
            _logger.LogInformation(
                "Correction learning unavailable in {App} code editor: Monaco hides the text from UI Automation " +
                "unless screen-reader mode is on. Set \"editor.accessibilitySupport\": \"on\" in its settings " +
                "(chat/composer inputs are read without it)",
                app);
            return;
        }

        _logger.LogInformation(
            "Correction learning unavailable in {App}: UI Automation cannot read the inserted text " +
            "(type={Type}, class={Class}, textPattern={Text}, valuePattern={Value}, valueLen={Len}, placeholder={Placeholder}, attempts={Attempts})",
            app, info.ControlType, info.ClassName, info.HasText, info.HasValue, info.ValueLength, info.IsPlaceholder, attempts);
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
            var text = DocumentText(element);
            if (!string.IsNullOrEmpty(text))
            {
                return Normalize(text);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
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

    // The typed RCW path for TextPattern.DocumentRange threw InvalidCastException on every call
    // (seen on Windows 11 with Cursor/Chromium), so TextPattern is read through raw vtable calls.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDocumentRangeFn(IntPtr self, out IntPtr range);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetTextFn(IntPtr self, int maxLength, out IntPtr bstr);

    private static readonly Guid IID_TextPattern = new("32eba289-3583-42c9-9c59-3b6d9a1e9b6a");

    private static string? DocumentText(IUIAutomationElement element)
    {
        if (element.GetCurrentPatternAs(UIA_TextPatternId, IID_TextPattern, out var pattern) < 0 || pattern == IntPtr.Zero)
        {
            return null;
        }

        var range = IntPtr.Zero;
        try
        {
            // IUIAutomationTextPattern: IUnknown(3) + RangeFromPoint, RangeFromChild, GetSelection, GetVisibleRanges, get_DocumentRange
            var getRange = Marshal.GetDelegateForFunctionPointer<GetDocumentRangeFn>(VtableSlot(pattern, 7));
            if (getRange(pattern, out range) < 0 || range == IntPtr.Zero)
            {
                return null;
            }

            // IUIAutomationTextRange: IUnknown(3) + Clone, Compare, CompareEndpoints, ExpandToEnclosingUnit, FindAttribute,
            // FindText, GetAttributeValue, GetBoundingRectangles, GetEnclosingElement, GetText
            var getText = Marshal.GetDelegateForFunctionPointer<GetTextFn>(VtableSlot(range, 12));
            if (getText(range, MaxTextLength, out var bstr) < 0 || bstr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringBSTR(bstr);
            }
            finally
            {
                Marshal.FreeBSTR(bstr);
            }
        }
        finally
        {
            if (range != IntPtr.Zero)
            {
                Marshal.Release(range);
            }

            Marshal.Release(pattern);
        }
    }

    private static IntPtr VtableSlot(IntPtr comObject, int slot) =>
        Marshal.ReadIntPtr(Marshal.ReadIntPtr(comObject), slot * IntPtr.Size);

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
        [PreserveSig]
        int GetCurrentPatternAs(int patternId, in Guid riid, out IntPtr patternObject);
        void GetCachedPatternAs();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object? GetCurrentPattern(int patternId);
    }
}
