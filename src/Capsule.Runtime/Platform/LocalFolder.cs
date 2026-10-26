namespace Capsule.Runtime.Platform;

// The per-user local folder a game's crash log and saves share. LocalApplicationData on its own maps
// macOS away from its convention. SDL's preference path roams on Windows, and every save written here
// would be orphaned by it.
internal static class LocalFolder
{
    internal const string SavesSubfolder = "saves";

    internal static string Resolve(string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        // The XDG spec ignores a relative XDG_DATA_HOME. An unset or empty HOME falls back to the
        // runtime's answer, not the working directory.
        string? xdg = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("XDG_DATA_HOME") : null;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string root = xdg is { Length: > 0 } && Path.IsPathRooted(xdg) ? xdg
            : OperatingSystem.IsLinux() && home.Length > 0 ? Path.Combine(home, ".local", "share")
            : OperatingSystem.IsMacOS() && home.Length > 0 ? Path.Combine(home, "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(root, folderName);
    }
}
