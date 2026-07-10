namespace echo.Platform.Linux;

public enum LinuxPackageManager
{
    Unknown,
    Apt,
    Dnf,
    Pacman,
}

public static class LinuxPackageManagerDetector
{
    public static LinuxPackageManager Detect()
    {
        if (!OperatingSystem.IsLinux())
        {
            return LinuxPackageManager.Unknown;
        }

        if (LinuxCommandHelper.CommandExists("apt-get"))
        {
            return LinuxPackageManager.Apt;
        }

        if (LinuxCommandHelper.CommandExists("dnf"))
        {
            return LinuxPackageManager.Dnf;
        }

        if (LinuxCommandHelper.CommandExists("pacman"))
        {
            return LinuxPackageManager.Pacman;
        }

        return LinuxPackageManager.Unknown;
    }

    public static bool CanElevateInstall() =>
        LinuxPackageManagerDetector.Detect() != LinuxPackageManager.Unknown
        && LinuxCommandHelper.CommandExists("pkexec");
}
