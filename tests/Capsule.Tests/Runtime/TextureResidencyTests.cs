using Capsule.Assets;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

// Where a handle's file has to be, and which handles a scene's set moves on and off the device.
// The decode needs a graphics device; neither the path contract nor the set arithmetic does.
public sealed class TextureResidencyTests
{
    private static readonly TextureHandle Hero = new("hero", ".png");

    private static readonly TextureHandle Tiles = new("tiles", ".png");

    // A handle's name is the source's path under the textures root, so a nested asset resolves to
    // a nested file — with the format's separator, whatever the platform's is.
    [Theory]
    [InlineData("hero", "assets/textures/hero.png")]
    [InlineData("enemies/bat", "assets/textures/enemies/bat.png")]
    public void AHandle_NamesItsFileUnderTheTexturesDomain(string name, string expected)
    {
        Assert.Equal(expected, TextureFiles.RelativePathOf(new TextureHandle(name, ".png")));
    }

    [Fact]
    public void Locate_FindsAShippedTextureUnderTheDirectoryItWasAuthoredIn()
    {
        TextureHandle bat = new("enemies/bat", ".png");
        using Shipped shipped = new(bat);

        Assert.Equal(shipped.Path, TextureFiles.Locate(shipped.BaseDirectory, bat));
    }

    [Fact]
    public void Locate_FailsNamingTheHandleAndThePathItLookedIn()
    {
        using Shipped shipped = new();

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => TextureFiles.Locate(shipped.BaseDirectory, Hero));

        Assert.Contains("'hero'", error.Message, StringComparison.Ordinal);
        Assert.Contains("assets/textures/hero.png", error.Message, StringComparison.Ordinal);
    }

    // Registries aggregate per logic assembly, so two of them may ship under one stem. That names
    // one file, and decoding it twice would strand the first texture on the device.
    [Fact]
    public void Resolve_LocatesEachHandleOnce_InFirstAppearanceOrder()
    {
        using Shipped shipped = new(Hero, Tiles);

        (TextureHandle Handle, string Path)[] resolved =
            TextureFiles.Resolve(shipped.BaseDirectory, [Tiles, Hero, Tiles]);

        Assert.Equal([Tiles, Hero], resolved.Select(static entry => entry.Handle));
        Assert.All(resolved, entry => Assert.True(File.Exists(entry.Path)));
    }

    [Fact]
    public void Resolve_FailsOnTheFirstHandleThatShipsNoFile()
    {
        using Shipped shipped = new(Hero);

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => TextureFiles.Resolve(shipped.BaseDirectory, [Hero, Tiles]));

        Assert.Contains("'tiles'", error.Message, StringComparison.Ordinal);
    }

    // A set replaces the last one: only the difference reaches the device.
    [Fact]
    public void ASet_LoadsWhatItAddsAndReleasesWhatItDrops()
    {
        TextureHandle shared = new("shared", ".png");
        Recorded recorded = new();

        recorded.Residency.MakeResident("Menu", [Hero, shared]);
        recorded.Residency.MakeResident("Arena", [shared, Tiles]);

        Assert.Equal([("Menu", "hero,shared", ""), ("Arena", "tiles", "hero")], recorded.Changes);
    }

    [Fact]
    public void AHandleNamedTwiceInOneSet_IsLoadedOnce()
    {
        Recorded recorded = new();

        recorded.Residency.MakeResident("Arena", [Hero, Tiles, Hero]);

        Assert.Equal([("Arena", "hero,tiles", "")], recorded.Changes);
    }

    // Two scenes over one set: the device has no work in that transition and is not disturbed.
    [Fact]
    public void ASetThatChangesNothing_NeverReachesTheDevice()
    {
        Recorded recorded = new();

        recorded.Residency.MakeResident("Menu", [Hero]);
        recorded.Residency.MakeResident("Arena", [Hero]);

        Assert.Equal([("Menu", "hero", "")], recorded.Changes);
    }

    // A decode that fails leaves the last scene's set accounted for, so the next set diffs against
    // what is actually on the device.
    [Fact]
    public void ASetTheDeviceRefuses_LeavesNothingRecorded()
    {
        Recorded recorded = new();
        recorded.Residency.MakeResident("Menu", [Hero]);
        recorded.Fails = true;

        Assert.Throws<FileNotFoundException>(() => recorded.Residency.MakeResident("Arena", [Tiles]));

        recorded.Fails = false;
        recorded.Residency.MakeResident("Arena", [Tiles]);

        Assert.Equal(("Arena", "tiles", "hero"), recorded.Changes[^1]);
    }

    [Fact]
    public void ADrawTheSetDoesNotCover_NamesTheSceneAndTheHandle()
    {
        string message = SceneResidency.NotResident("Arena", Hero);

        Assert.Contains("'Arena'", message, StringComparison.Ordinal);
        Assert.Contains("'hero'", message, StringComparison.Ordinal);
        Assert.Contains("assets/textures/hero.png", message, StringComparison.Ordinal);
    }

    private sealed class Recorded
    {
        internal Recorded() => Residency = new SceneResidency(Apply);

        internal SceneResidency Residency { get; }

        /// <summary>Each change as (scene, loaded, released), the handles ordinal-joined.</summary>
        internal List<(string Scene, string Load, string Release)> Changes { get; } = [];

        internal bool Fails { get; set; }

        private void Apply(string scene, IReadOnlyList<TextureHandle> load, IReadOnlyList<TextureHandle> release)
        {
            if (Fails)
            {
                throw new FileNotFoundException("the device refused the set");
            }

            Changes.Add((scene, Names(load), Names(release)));
        }

        private static string Names(IReadOnlyList<TextureHandle> handles) =>
            string.Join(",", handles.Select(static handle => handle.Name).Order(StringComparer.Ordinal));
    }

    private sealed class Shipped : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("capsule-textures-");

        internal Shipped(params TextureHandle[] textures)
        {
            foreach (TextureHandle handle in textures)
            {
                Path = System.IO.Path.Combine(BaseDirectory, TextureFiles.RelativePathOf(handle));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllBytes(Path, []);
            }
        }

        internal string BaseDirectory => _directory.FullName;

        /// <summary>The last file shipped, which is the only one the single-handle specs ship.</summary>
        internal string Path { get; private set; } = string.Empty;

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
