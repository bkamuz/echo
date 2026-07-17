using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

public sealed class LinuxCursorPosition : ICursorPosition
{
    public bool TryGetPosition(out int x, out int y)
    {
        x = 0;
        y = 0;

        if (!LinuxCommandHelper.CommandExists("xdotool"))
        {
            return false;
        }

        try
        {
            var output = LinuxProcessRunner.RunCommand(
                "xdotool",
                ["getmouselocation", "--shell"],
                CancellationToken.None,
                allowFailure: true,
                timeoutMs: 500);

            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            int? parsedX = null;
            int? parsedY = null;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("X=", StringComparison.Ordinal) && int.TryParse(line.AsSpan(2), out var px))
                {
                    parsedX = px;
                }
                else if (line.StartsWith("Y=", StringComparison.Ordinal) && int.TryParse(line.AsSpan(2), out var py))
                {
                    parsedY = py;
                }
            }

            if (parsedX is null || parsedY is null)
            {
                return false;
            }

            x = parsedX.Value;
            y = parsedY.Value;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
