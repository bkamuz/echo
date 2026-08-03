using echo.Abstractions.Core;

namespace echo.Core.Tests;

internal static class GigaAmTestFixtures
{
    public static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "echo-gigaam-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void WriteBundle(string dir, string prefix)
    {
        File.WriteAllText(Path.Combine(dir, $"{prefix}_encoder.onnx"), "");
        File.WriteAllText(Path.Combine(dir, $"{prefix}_decoder.onnx"), "");
        File.WriteAllText(Path.Combine(dir, $"{prefix}_joint.onnx"), "");
        File.WriteAllText(Path.Combine(dir, $"{prefix}_tokens.txt"), "");
    }

    public static void WriteCtc(string dir, string prefix, bool int8 = true)
    {
        File.WriteAllText(Path.Combine(dir, $"{prefix}_tokens.txt"), "");
        File.WriteAllText(
            Path.Combine(dir, int8 ? $"{prefix}_int8.onnx" : $"{prefix}.onnx"),
            "");
    }
}
