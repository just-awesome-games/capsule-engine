using Capsule.Runtime;
using Capsule.Runtime.Persistence;
using Capsule.Runtime.Platform;

namespace Capsule.Tests.Runtime;

public sealed class HostPlatformTests
{
    // Every save a player already has stays where docs/persistence.md says it is.
    [Fact]
    public void TheDefaultSaves_AreInTheLocalFolderTheDocsName()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string root = OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Application Support")
            : xdg is { Length: > 0 } && Path.IsPathRooted(xdg) ? xdg
            : Path.Combine(home, ".local", "share");

        DirectorySaveStorage saves = Assert.IsType<DirectorySaveStorage>(new HostPlatform().OpenSaveStorage("my-game"));

        Assert.Equal(Path.Combine(root, "my-game", "saves"), saves.Directory);
    }

    // SDL3 answers with an array of pointers to locales. Read as an array of locales, the tags come
    // back as pointer bytes or the read faults.
    [Fact]
    public void PreferredLocales_AreLanguageTags_ReadFromSdl3()
    {
        Assert.All(Sdl3.PreferredLocales(), tag => Assert.Matches("^[A-Za-z]{1,8}(-[A-Za-z0-9]{1,8})?$", tag));
    }
}
