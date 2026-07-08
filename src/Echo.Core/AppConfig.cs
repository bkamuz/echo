using System.Text.Json;
using System.Text.Json.Serialization;
using echo.Abstractions.Core;

namespace echo.Core;

public sealed class AppConfig
{
    public static IReadOnlyList<string> WhisperSizes => ModelRegistry.WhisperSizes;

    public static IReadOnlyList<string> Engines { get; } = ["gigaam", "whisper"];
    public static IReadOnlyList<string> Devices { get; } = ["cpu", "cuda"];

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "ctrl+cmd";

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "gigaam";

    [JsonPropertyName("whisper_model_size")]
    public string WhisperModelSize { get; set; } = "small";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "ru";

    [JsonPropertyName("device")]
    public string Device { get; set; } = "cpu";

    [JsonPropertyName("input_device")]
    public string InputDevice { get; set; } = string.Empty;

    [JsonPropertyName("input_method")]
    public string InputMethod { get; set; } = "type";

    [JsonPropertyName("min_press_ms")]
    public int MinPressMs { get; set; } = 300;

    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; set; } = 16000;

    [JsonPropertyName("add_trailing_space")]
    public bool AddTrailingSpace { get; set; } = true;

    [JsonPropertyName("extra")]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();

    public string ComputeType => Device == "cuda" ? "float16" : "int8";

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

        if (!Devices.Contains(Device))
        {
            Device = "cpu";
        }
    }
}
