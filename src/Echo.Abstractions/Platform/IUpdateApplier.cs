using echo.Abstractions.Core;

namespace echo.Abstractions.Platform;

public interface IUpdateApplier
{
    bool IsSupported { get; }

    Task ApplyAndRestartAsync(
        UpdateInfo update,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
