namespace echo.Abstractions.Core;

public static class NativeExitCodes
{
    public static string Describe(int exitCode)
    {
        if (exitCode == 0)
        {
            return "success";
        }

        var unsigned = unchecked((uint)exitCode);
        var known = unsigned switch
        {
            0xC0000005 => "STATUS_ACCESS_VIOLATION (native crash — likely Sherpa/ORT DLL or model init)",
            0xC0000135 => "STATUS_DLL_NOT_FOUND (missing native dependency or VC++ runtime)",
            0xC0000139 => "STATUS_ENTRYPOINT_NOT_FOUND (DLL mismatch / wrong architecture)",
            0xC0000409 => "STATUS_STACK_BUFFER_OVERRUN",
            0xC000001D => "STATUS_ILLEGAL_INSTRUCTION",
            0x80004005 => "E_FAIL",
            _ => null,
        };

        return known is null
            ? $"exit code {exitCode} (0x{unsigned:X8})"
            : $"{known}; code={exitCode} (0x{unsigned:X8})";
    }
}
