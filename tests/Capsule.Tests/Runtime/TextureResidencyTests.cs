using Capsule.Assets;
using Capsule.Runtime.Assets;

namespace Capsule.Tests.Runtime;

// Where a handle's file has to be, and how scene-owned assets load and leave memory. Texture decode
// needs a graphics device; neither the path contract nor generic ownership does.
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

        Assert.Equal(System.IO.Path.GetFullPath(shipped.Path), TextureFiles.Locate(shipped.BaseDirectory, bat));
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

    [Fact]
    public void Locate_RejectsAHandleThatWouldResolveOutsideTheTexturesRoot()
    {
        using Shipped shipped = new();
        string outside = System.IO.Path.Combine(shipped.BaseDirectory, "assets", "outside.png");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outside)!);
        File.WriteAllBytes(outside, []);
        TextureHandle escaping = new("../outside", ".png");

        Assert.Throws<ArgumentException>(() => TextureFiles.Locate(shipped.BaseDirectory, escaping));
    }

    [Fact]
    public void Locate_RejectsTheDefaultHandleBeforeLookingForAFile()
    {
        using Shipped shipped = new();

        Assert.Throws<ArgumentException>(() => TextureFiles.Locate(shipped.BaseDirectory, default));
    }

    [Fact]
    public void Get_LoadsOnceAndReusesTheAssetOnHits()
    {
        int loads = 0;
        using SceneAssetStore<TextureHandle, FakeTexture> store = new(handle =>
        {
            loads++;
            return new FakeTexture(handle.Name);
        });

        FakeTexture first = store.Get(Hero);
        FakeTexture second = store.Get(Hero);

        Assert.Same(first, second);
        Assert.Equal(1, loads);
    }

    [Fact]
    public void AnAssetNotPreloaded_LoadsOnFirstUse()
    {
        using SceneAssetStore<TextureHandle, FakeTexture> store = new(
            static handle => new FakeTexture(handle.Name));
        store.ChangeScene([]);

        FakeTexture loaded = store.Get(Hero);

        Assert.Equal("hero", loaded.Name);
        Assert.Same(loaded, store.Get(Hero));
    }

    [Fact]
    public void ChangingScene_ReusesSharedAssetsAndReleasesOldPreloadsAndLazyLoads()
    {
        TextureHandle sharedHandle = new("shared", ".png");
        TextureHandle nextHandle = new("next", ".png");
        using SceneAssetStore<TextureHandle, FakeTexture> store = new(
            static handle => new FakeTexture(handle.Name));
        store.ChangeScene([Hero, sharedHandle]);
        FakeTexture hero = store.Get(Hero);
        FakeTexture shared = store.Get(sharedHandle);
        FakeTexture lazy = store.Get(Tiles);

        store.ChangeScene([sharedHandle, nextHandle]);

        Assert.True(hero.Disposed);
        Assert.True(lazy.Disposed);
        Assert.False(shared.Disposed);
        Assert.Same(shared, store.Get(sharedHandle));
        Assert.Equal("next", store.Get(nextHandle).Name);
    }

    [Fact]
    public void APreloadFailure_RetainsPriorAssetsAndDisposesStagedAssets()
    {
        TextureHandle stagedHandle = new("staged", ".png");
        FakeTexture? staged = null;
        bool fail = false;
        using SceneAssetStore<TextureHandle, FakeTexture> store = new(handle =>
        {
            if (fail && handle == Tiles)
            {
                throw new InvalidDataException("decode failed");
            }

            FakeTexture loaded = new(handle.Name);
            if (handle == stagedHandle)
            {
                staged = loaded;
            }

            return loaded;
        });
        store.ChangeScene([Hero]);
        FakeTexture hero = store.Get(Hero);
        FakeTexture lazy = store.Get(new TextureHandle("lazy", ".png"));
        fail = true;

        Assert.Throws<InvalidDataException>(
            () => store.ChangeScene([stagedHandle, Tiles]));

        Assert.Same(hero, store.Get(Hero));
        Assert.Same(lazy, store.Get(new TextureHandle("lazy", ".png")));
        Assert.False(hero.Disposed);
        Assert.False(lazy.Disposed);
        Assert.True(staged!.Disposed);
    }

    [Fact]
    public void Dispose_ReleasesEveryAssetOwnedByTheScene()
    {
        SceneAssetStore<TextureHandle, FakeTexture> store = new(
            static handle => new FakeTexture(handle.Name));
        store.ChangeScene([Hero]);
        FakeTexture preload = store.Get(Hero);
        FakeTexture lazy = store.Get(Tiles);

        store.Dispose();

        Assert.True(preload.Disposed);
        Assert.True(lazy.Disposed);
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

    private sealed class FakeTexture(string name) : IDisposable
    {
        internal string Name => name;

        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
