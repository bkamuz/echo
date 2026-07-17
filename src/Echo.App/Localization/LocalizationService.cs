using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using echo.Abstractions.Core;

namespace echo.App.Localization;

/// <summary>
/// UI localization via Avalonia ResourceDictionaries.
/// To add a language: copy Resources/i18n/ru.axaml → {code}.axaml, translate values,
/// add the code to <see cref="ExplicitLanguageCodes"/>.
/// </summary>
public sealed class LocalizationService
{
    public const string SystemLanguage = "system";
    public const string FallbackLanguage = "ru";

    /// <summary>Explicit language packs (not including "system").</summary>
    public static IReadOnlyList<string> ExplicitLanguageCodes { get; } = ["ru", "en"];

    private ResourceDictionary? _activeDictionary;
    private string _resolvedLanguage = FallbackLanguage;
    private string _preference = SystemLanguage;

    public event EventHandler? LanguageChanged;

    public string Preference => _preference;

    public string ResolvedLanguage => _resolvedLanguage;

    /// <summary>Code + display-name Loc key for the settings ComboBox.</summary>
    public IReadOnlyList<(string Code, string DisplayNameKey)> LanguageOptions { get; } =
    [
        (SystemLanguage, "Loc.UiLang.System"),
        ("ru", "Loc.UiLang.Russian"),
        ("en", "Loc.UiLang.English"),
    ];

    public void Apply(string? uiLanguage)
    {
        var preference = string.IsNullOrWhiteSpace(uiLanguage) ? SystemLanguage : uiLanguage.Trim();
        if (!string.Equals(preference, SystemLanguage, StringComparison.OrdinalIgnoreCase)
            && !ExplicitLanguageCodes.Contains(preference, StringComparer.OrdinalIgnoreCase))
        {
            preference = SystemLanguage;
        }

        var resolved = ResolveLanguage(preference);
        var normalizedPreference = preference.Equals(SystemLanguage, StringComparison.OrdinalIgnoreCase)
            ? SystemLanguage
            : resolved;

        var changed = !string.Equals(_preference, normalizedPreference, StringComparison.Ordinal)
            || !string.Equals(_resolvedLanguage, resolved, StringComparison.OrdinalIgnoreCase)
            || _activeDictionary is null;

        _preference = normalizedPreference;
        if (!changed)
        {
            return;
        }

        SwapDictionary(resolved);
        _resolvedLanguage = resolved;

        try
        {
            var culture = CultureInfo.GetCultureInfo(resolved);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(FallbackLanguage);
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        if (_activeDictionary is not null
            && _activeDictionary.TryGetValue(key, out var fromActive)
            && fromActive is string activeString)
        {
            return activeString;
        }

        return key;
    }

    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentUICulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>Resolve Loc.* (and multi-key via U+001F); pass through plain text.</summary>
    public string LocText(string? keyOrText)
    {
        if (string.IsNullOrEmpty(keyOrText))
        {
            return string.Empty;
        }

        if (keyOrText.Contains('\u001f'))
        {
            return string.Join(
                " ",
                keyOrText.Split('\u001f', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(LocText));
        }

        return keyOrText.StartsWith("Loc.", StringComparison.Ordinal) ? Get(keyOrText) : keyOrText;
    }

    public string LocalizeProgress(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return string.Empty;
        }

        if (ProgressMessages.IsDone(status))
        {
            var detail = ProgressMessages.GetDoneDetail(status);
            return string.IsNullOrEmpty(detail)
                ? Get("Loc.Status.Done")
                : Format("Loc.Status.DoneWithDetail", detail);
        }

        if (ProgressMessages.TryParseWorking(status, out var kind, out var arg))
        {
            return kind switch
            {
                "Downloading" => Format("Loc.Status.Downloading", arg ?? string.Empty),
                "Saving" => Get("Loc.Status.Saving"),
                "LoadingModel" => Get("Loc.Status.LoadingModel"),
                "PreparingDirectMl" => Get("Loc.Status.PreparingDirectMl"),
                "DownloadingUpdate" => Get("Loc.Status.DownloadingUpdate"),
                "PreparingUpdate" => Get("Loc.Status.PreparingUpdate"),
                "InstallingUpdate" => Get("Loc.Status.InstallingUpdate"),
                _ => status,
            };
        }

        return status;
    }

    public static string ResolveLanguage(string preference)
    {
        if (!string.Equals(preference, SystemLanguage, StringComparison.OrdinalIgnoreCase))
        {
            var explicitCode = preference.Trim().ToLowerInvariant();
            return ExplicitLanguageCodes.Contains(explicitCode, StringComparer.Ordinal)
                ? explicitCode
                : FallbackLanguage;
        }

        var ui = CultureInfo.CurrentUICulture;
        var twoLetter = ui.TwoLetterISOLanguageName;
        if (ExplicitLanguageCodes.Contains(twoLetter, StringComparer.OrdinalIgnoreCase))
        {
            return twoLetter.ToLowerInvariant();
        }

        var name = ui.Name;
        foreach (var code in ExplicitLanguageCodes)
        {
            if (name.StartsWith(code, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        return FallbackLanguage;
    }

    private void SwapDictionary(string languageCode)
    {
        var app = Application.Current;
        if (app?.Resources is not ResourceDictionary root)
        {
            return;
        }

        var merged = root.MergedDictionaries;
        if (_activeDictionary is not null)
        {
            merged.Remove(_activeDictionary);
        }

        _activeDictionary = LoadDictionary(languageCode);
        merged.Add(_activeDictionary);
    }

    private static ResourceDictionary LoadDictionary(string languageCode)
    {
        var uri = new Uri($"avares://echo.App/Resources/i18n/{languageCode}.axaml");
        if (AvaloniaXamlLoader.Load(uri) is ResourceDictionary dictionary)
        {
            return dictionary;
        }

        throw new InvalidOperationException($"Missing localization dictionary: {languageCode}");
    }
}
