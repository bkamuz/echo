using System.Text.Json;
using System.Text.Json.Serialization;
using echo.Abstractions.Core;
using echo.Abstractions.Engines;

namespace echo.Core;

public sealed class AppConfig
{
    public static IReadOnlyList<string> WhisperSizes => ModelRegistry.WhisperSizes;
    public static IReadOnlyList<string> GigaAmSizes => ModelRegistry.GigaAmSizes;

    public static IReadOnlyList<string> Engines { get; } = ["gigaam", "whisper", "omnilingual"];
    public static IReadOnlyList<string> Devices => ExecutionProviderResolver.AllDeviceIds;

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "ctrl+cmd";

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "gigaam";

    [JsonPropertyName("whisper_model_size")]
    public string WhisperModelSize { get; set; } = "small";

    [JsonPropertyName("gigaam_model_size")]
    public string GigaAmModelSize { get; set; } = "e2e";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "ru";

    /// <summary>UI language: system | ru | en (STT language is <see cref="Language"/>).</summary>
    [JsonPropertyName("ui_language")]
    public string UiLanguage { get; set; } = "system";

    [JsonPropertyName("device")]
    public string Device { get; set; } = "cpu";

    [JsonPropertyName("input_device")]
    public string InputDevice { get; set; } = string.Empty;

    [JsonPropertyName("input_method")]
    public string InputMethod { get; set; } = OperatingSystem.IsLinux() ? "auto" : "clipboard";

    [JsonPropertyName("type_delay_ms")]
    public int TypeDelayMs { get; set; } = 1;

    [JsonPropertyName("min_press_ms")]
    public int MinPressMs { get; set; } = 300;

    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; set; } = 16000;

    [JsonPropertyName("add_trailing_space")]
    public bool AddTrailingSpace { get; set; } = true;

    [JsonPropertyName("show_dictation_toast")]
    public bool ShowDictationToast { get; set; } = true;

    [JsonPropertyName("start_with_system")]
    public bool StartWithSystem { get; set; }

    [JsonPropertyName("last_update_check_utc")]
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    [JsonPropertyName("pending_update_version")]
    public string? PendingUpdateVersion { get; set; }

    [JsonPropertyName("pending_update_download_url")]
    public string? PendingUpdateDownloadUrl { get; set; }

    [JsonPropertyName("pending_update_release_notes_url")]
    public string? PendingUpdateReleaseNotesUrl { get; set; }

    [JsonPropertyName("extra")]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();

    public AppConfig Clone()
    {
        return new AppConfig
        {
            Hotkey = Hotkey,
            Engine = Engine,
            WhisperModelSize = WhisperModelSize,
            GigaAmModelSize = GigaAmModelSize,
            Language = Language,
            UiLanguage = UiLanguage,
            Device = Device,
            InputDevice = InputDevice,
            InputMethod = InputMethod,
            TypeDelayMs = TypeDelayMs,
            MinPressMs = MinPressMs,
            SampleRate = SampleRate,
            AddTrailingSpace = AddTrailingSpace,
            ShowDictationToast = ShowDictationToast,
            StartWithSystem = StartWithSystem,
            LastUpdateCheckUtc = LastUpdateCheckUtc,
            PendingUpdateVersion = PendingUpdateVersion,
            PendingUpdateDownloadUrl = PendingUpdateDownloadUrl,
            PendingUpdateReleaseNotesUrl = PendingUpdateReleaseNotesUrl,
            Extra = new Dictionary<string, JsonElement>(Extra),
        };
    }

    public void Normalize()
    {
        if (!Engines.Contains(Engine))
        {
            Engine = "gigaam";
        }

        if (!WhisperSizes.Contains(WhisperModelSize))
        {
            WhisperModelSize = "small";
        }

        if (GigaAmModelSize is "v3" or "v3-punct")
        {
            GigaAmModelSize = "e2e";
        }
        else if (!GigaAmSizes.Contains(GigaAmModelSize))
        {
            GigaAmModelSize = "e2e";
        }

        if (Device == "cuda")
        {
            Device = ExecutionProviderResolver.CpuDevice;
        }

        if (!Devices.Contains(Device))
        {
            Device = ExecutionProviderResolver.CpuDevice;
        }

        if (string.IsNullOrWhiteSpace(UiLanguage))
        {
            UiLanguage = "system";
        }
        else
        {
            var ui = UiLanguage.Trim().ToLowerInvariant();
            UiLanguage = ui is "system" or "ru" or "en" ? ui : "system";
        }

        TypeDelayMs = Math.Clamp(TypeDelayMs, 0, 50);

        if (OperatingSystem.IsLinux())
        {
            if (InputMethod is "type")
            {
                InputMethod = "auto";
            }
            else if (InputMethod is not ("auto" or "clipboard"))
            {
                InputMethod = "auto";
            }
        }
    }
}

