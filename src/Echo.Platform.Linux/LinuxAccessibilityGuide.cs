using System.Diagnostics;

namespace echo.Platform.Linux;

public static class LinuxAccessibilityGuide
{
    public static string GetInstructions()
    {
        if (LinuxSession.IsGnome && LinuxSession.IsWayland)
        {
            return "Loc.Linux.A11y.Instructions.GnomeWayland";
        }

        if (LinuxSession.IsGnome)
        {
            return "Loc.Linux.A11y.Instructions.Gnome";
        }

        if (LinuxSession.IsKde)
        {
            return "Loc.Linux.A11y.Instructions.Kde";
        }

        return "Loc.Linux.A11y.Instructions.Generic";
    }

    public static string Limitations => "Loc.Linux.A11y.Limitations";

    public static bool TryOpenSettings()
    {
        try
        {
            if (LinuxSession.IsGnome && LinuxCommandHelper.CommandExists("gnome-control-center"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "gnome-control-center",
                    Arguments = "universal-access",
                    UseShellExecute = false,
                });
                return true;
            }

            if (LinuxSession.IsKde && LinuxCommandHelper.CommandExists("systemsettings"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "systemsettings",
                    Arguments = "kcm_accessibility",
                    UseShellExecute = false,
                });
                return true;
            }

            if (LinuxCommandHelper.CommandExists("xdg-open"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = "settings://",
                    UseShellExecute = false,
                });
                return true;
            }
        }
        catch
        {
        }

        return false;
    }
}
