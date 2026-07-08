using System.Text.Json;
using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;

namespace echo.Core;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private readonly ILogger<ConfigStore> _logger;

    public ConfigStore(ILogger<ConfigStore> logger)
    {
        _logger = logger;
    }

    public AppConfig Load()
    {
        AppPaths.EnsureDirectories();

        if (!File.Exists(AppPaths.ConfigPath))
        {
            var defaults = new AppConfig();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            config.Normalize();
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Broken config.json, creating defaults");
            var defaults = new AppConfig();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(AppConfig config)
    {
        AppPaths.EnsureDirectories();
        config.Normalize();

        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(AppPaths.ConfigPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save config.json");
        }
    }
}
