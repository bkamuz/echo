using System.Text.Json;
using System.Text.Json.Serialization;
using echo.Abstractions.Core;

namespace echo.Core;

public sealed class AppConfig
{
    public static IReadOnlyList<string> WhisperSizes => ModelRegistry.WhisperSizes;
    public static IReadOnlyList<string> GigaAmSizes => ModelRegistry.GigaAmSizes;

    public static IReadOnlyList<string> Engines { get; } = ["gigaam", "whisper", "omnilingual"];
    public static IReadOnlyList<string> Devices { get; } = ["cpu", "cuda"];

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

    [JsonPropertyName("device")]
    public string Device { get; set; } = "cpu";

    [JsonPropertyName("input_device")]
    public string InputDevice { get; set; } = string.Empty;

    [JsonPropertyName("input_method")]
    public string InputMethod { get; set; } = "clipboard";

    [JsonPropertyName("type_delay_ms")]
    public int TypeDelayMs { get; set; } = 1;

    [JsonPropertyName("min_press_ms")]
    public int MinPressMs { get; set; } = 300;

    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; set; } = 16000;

    [JsonPropertyName("add_trailing_space")]
    public bool AddTrailingSpace { get; set; } = true;

    [JsonPropertyName("extra")]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();

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

        if (!Devices.Contains(Device))
        {
            Device = "cpu";
        }

        TypeDelayMs = Math.Clamp(TypeDelayMs, 0, 50);
    }
}
