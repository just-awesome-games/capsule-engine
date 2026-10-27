using System.Reflection;
using System.Reflection.Emit;
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

    // A player's crash log names the build its frames decode against before the exception: the
    // time, the game's and Capsule's versions, the system and the executable's hash.
    [Fact]
    public void TheCrashLog_HeadsTheExceptionWithTheBuild()
    {
        InvalidOperationException failure = new("spec failure");
        DateTimeOffset now = new(2026, 10, 27, 15, 53, 12, TimeSpan.Zero);

        string[] lines = CrashLog.Text("My Game", now, failure).Split(Environment.NewLine);

        Assert.Equal("2026-10-27T15:53:12.0000000+00:00", lines[0]);
        Assert.StartsWith("My Game ", lines[1], StringComparison.Ordinal);
        Assert.Equal($"Capsule {BuildIdentity.Version(typeof(HostPlatform).Assembly)}", lines[2]);
        Assert.DoesNotContain("unknown", lines[2], StringComparison.Ordinal);
        Assert.Matches("^.+ sha256 [0-9a-f]{64}$", lines[4]);
        Assert.Equal(failure.ToString(), lines[5]);
    }

    // The hash is of the executable's bytes, and an executable that cannot be read is named unknown
    // rather than failing the crash report.
    [Fact]
    public void TheBuildsExecutable_IsNamedByItsSha256()
    {
        using TempWorkspace workspace = new("capsule-build-identity");
        string executable = Path.Combine(workspace.Root, "MyGame.exe");
        File.WriteAllText(executable, "abc");

        Assert.Equal("MyGame.exe sha256 ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", BuildIdentity.Executable(executable));
        Assert.Equal("Gone.exe sha256 unknown", BuildIdentity.Executable(Path.Combine(workspace.Root, "Gone.exe")));
        Assert.Equal("unknown sha256 unknown", BuildIdentity.Executable(null));
    }

    // Only the informational version names a commit. An assembly built without one reads unknown, never
    // a bare assembly version that would pass for it.
    [Fact]
    public void ABuildsVersion_IsItsInformationalVersionOrUnknown()
    {
        AssemblyBuilder bare = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Bare") { Version = new Version(1, 2, 3) }, AssemblyBuilderAccess.Run);

        Assert.Equal("unknown", BuildIdentity.Version(bare));
        Assert.Equal("unknown", BuildIdentity.Version(null));
    }

    // SDL3 answers with an array of pointers to locales. Read as an array of locales, the tags come
    // back as pointer bytes or the read faults.
    [Fact]
    public void PreferredLocales_AreLanguageTags_ReadFromSdl3()
    {
        Assert.All(Sdl3.PreferredLocales(), tag => Assert.Matches("^[A-Za-z]{1,8}(-[A-Za-z0-9]{1,8})?$", tag));
    }
}
