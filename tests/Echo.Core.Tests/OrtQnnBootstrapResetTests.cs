using echo.Engines.ParakeetNpu.Ort;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.Core.Tests;

public class OrtQnnBootstrapResetTests
{
    [Fact]
    public void ResetPartialState_DoesNotThrow_WhenNothingInitialized()
    {
        var exception = Record.Exception(() =>
            OrtQnnBootstrap.ResetPartialState(NullLogger.Instance));

        Assert.Null(exception);
    }
}
