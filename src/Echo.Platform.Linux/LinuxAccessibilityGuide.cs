using System.Diagnostics;

namespace echo.Platform.Linux;

public static class LinuxAccessibilityGuide
{
    public static string GetInstructions()
    {
        if (LinuxSession.IsGnome && LinuxSession.IsWayland)
        {
            return "GNOME Wayland: включите «Настройки» → «Специальные возможности» → "
                + "«Вспомогательные технологии» (Assistive Technology Support). "
                + "Тогда Echo вставляет текст напрямую через AT-SPI — без буфера обмена и без мигания индикатора на панели. "
                + "Если спец. возможности выключены, используется буфер + Ctrl+V (ydotool) — появится значок буфера.";
        }

        if (LinuxSession.IsGnome)
        {
            return "GNOME: откройте «Настройки» → «Специальные возможности» и включите доступ для приложений. "
                + "Без этого AT-SPI не сможет вставлять текст в другие окна.";
        }

        if (LinuxSession.IsKde)
        {
            return "KDE: откройте «Параметры системы» → «Специальные возможности» и включите поддержку AT-SPI.";
        }

        return "Включите специальные возможности (Accessibility / Assistive Technologies) в настройках вашего окружения. "
            + "Echo использует AT-SPI для автоматической вставки текста.";
    }

    public static string Limitations =>
        "Хорошо работает в браузерах, офисных приложениях и большинстве полей ввода. "
        + "В некоторых играх, ncurses-терминалах и sandbox-приложениях останется только копирование в буфер.";

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
