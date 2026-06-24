using System;
using System.IO;

namespace Muselly.App.Services;

/// <summary>
/// Resolves the per-user config directory using each OS's standard location:
/// Windows <c>%AppData%\Muselly</c>, macOS <c>~/Library/Application Support/Muselly</c>,
/// Linux <c>$XDG_CONFIG_HOME/Muselly</c> (falling back to <c>~/.config/Muselly</c>).
/// </summary>
public static class AppPaths
{
    private const string AppFolder = "Muselly";

    public static string SettingsFile()
    {
        var dir = ConfigDirectory();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static string ConfigDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);

        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", AppFolder);

        // Linux / other: honour XDG_CONFIG_HOME, else ~/.config.
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(xdg))
            xdg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(xdg, AppFolder);
    }
}
