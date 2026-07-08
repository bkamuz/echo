namespace echo.Abstractions.Core;

public static class AppPaths
{
    public static string BaseDir
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "Echo");
            }

            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    "Echo");
            }

            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var configRoot = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdg;
            return Path.Combine(configRoot, "echo");
        }
    }

    public static string ModelsDir => Path.Combine(BaseDir, "models");
    public static string ConfigPath => Path.Combine(BaseDir, "config.json");
    public static string HistoryPath => Path.Combine(BaseDir, "history.jsonl");
    public static string LogPath => Path.Combine(BaseDir, "echo.log");

    public static string WhisperDir(string modelSize) => Path.Combine(ModelsDir, "whisper", modelSize);
    public static string GigaAmDir => Path.Combine(ModelsDir, "gigaam-v3");
    public static string OmnilingualDir => Path.Combine(ModelsDir, "omnilingual-300m");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(ModelsDir);
    }
}
