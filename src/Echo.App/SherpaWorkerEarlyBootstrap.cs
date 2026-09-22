using System.Runtime.CompilerServices;
using echo.Core.Diagnostics;

namespace echo.App;

/// <summary>
/// Runs as soon as Echo.App loads so we can tell whether managed bootstrap reached
/// the entry assembly before Main (and before any Sherpa worker command handling).
/// </summary>
internal static class SherpaWorkerEarlyBootstrap
{
    [ModuleInitializer]
    internal static void Run()
    {
        if (!SherpaWorkerDiagnostics.IsWorkerProcess())
        {
            return;
        }

        SherpaWorkerDiagnostics.RegisterUnhandledExceptionHandlersIfWorker();
        SherpaWorkerDiagnostics.WriteProcessStart("module-initializer");
    }
}
