using echo.Abstractions.Core;

namespace echo.Abstractions.Platform;

public sealed class UnsupportedUpdateApplier : IUpdateApplier
{
    public bool IsSupported => false;

    public Task ApplyAndRestartAsync(
        UpdateInfo update,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new PlatformNotSupportedException("In-app updates are not supported on this platform."));
}
