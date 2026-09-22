using Capsule.Assets;
using Capsule.Runtime.Assets;

namespace Capsule.Tests.Runtime;

// Where a handle's file has to be, and how scene-owned assets load and leave memory. Texture decode
// needs a graphics device; neither the content-path contract nor generic ownership does.
public sealed class TextureResidencyTests
{
    private static readonly TextureHandle Hero = new("hero", ".png");

    private static readonly TextureHandle Tiles = new("tiles", ".png");

    // A handle's name is the source's path under its own root, so a nested asset resolves to a
    // nested file — with the format's separator, whatever the platform's is — and a bitmap font's
    // pages ship under the fonts root beside the font they were cut for. What the handle names is
    // what Open reads once the file ships there.
    [Theory]
    [InlineData("hero", false, "assets/textures/hero.png")]
    [InlineData("enemies/bat", false, "assets/textures/enemies/bat.png")]
    [InlineData("ui/menu", true, "assets/fonts/ui/menu.png")]
    public void AHandle_NamesAndLocatesItsFileUnderItsOwnDomain(string name, bool fontPage, string expected)
    {
        TextureHandle handle = fontPage ? TextureHandle.FontPage(name, ".png") : new TextureHandle(name, ".png");
        using Shipped shipped = new(handle);

        Assert.Equal(expected, TextureFiles.RelativePathOf(handle));
        using Stream opened = TextureFiles.Open(shipped.Platform, handle);
        Assert.Equal(System.IO.Path.GetFullPath(shipped.Path), Assert.IsType<FileStream>(opened).Name);
    }

    // One name under two roots is two files and therefore two textures, which the store has to keep
    // apart.
    [Fact]
    public void ATextureAndAFontPageOfOneName_AreNotOneHandle()
    {
        TextureHandle texture = new("menu", ".png");
        TextureHandle page = TextureHandle.FontPage("menu", ".png");

        Assert.NotEqual(texture, page);
        Assert.Equal(page, TextureHandle.FontPage("menu", ".png"));
    }

    [Fact]
    public void Open_FailsNamingTheHandleAndThePathItLookedIn()
    {
        using Shipped shipped = new();

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => TextureFiles.Open(shipped.Platform, Hero));

        Assert.Contains("'hero'", error.Message, StringComparison.Ordinal);
        Assert.Contains("assets/textures/hero.png", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_RejectsAHandleThatWouldResolveOutsideTheTexturesRoot()
    {
        using Shipped shipped = new();
        string outside = System.IO.Path.Combine(shipped.BaseDirectory, "assets", "outside.png");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outside)!);
        File.WriteAllBytes(outside, []);
        TextureHandle escaping = new("../outside", ".png");

        Assert.Throws<ArgumentException>(() => TextureFiles.Open(shipped.Platform, escaping));
    }

    [Fact]
    public void Open_RejectsTheDefaultHandleBeforeLookingForAFile()
    {
        using Shipped shipped = new();

        Assert.Throws<ArgumentException>(() => TextureFiles.Open(shipped.Platform, default));
    }

    [Fact]
    public void Get_LoadsOnceAndReusesTheAssetOnHits()
    {
        int loads = 0;
        using SceneAssetStore<TextureHandle, FakeTexture> store = SyncStore.Over<TextureHandle, FakeTexture>(handle =>
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
    public void ChangingScene_ReusesSharedAssetsAndReleasesOldPreloadsAndLazyLoads()
    {
        TextureHandle sharedHandle = new("shared", ".png");
        TextureHandle nextHandle = new("next", ".png");
        using SceneAssetStore<TextureHandle, FakeTexture> store = SyncStore.Over<TextureHandle, FakeTexture>(
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

    // Pins that a transition leaks nothing: the store releases the outgoing scene's texture as it
    // takes the incoming one, so residency after the first transition never grows.
    [Fact]
    public void AHundredTransitionsBetweenTwoScenes_LeaveResidencyWhereTheFirstTransitionPutIt()
    {
        List<FakeTexture> loaded = [];
        using SceneAssetStore<TextureHandle, FakeTexture> store = SyncStore.Over<TextureHandle, FakeTexture>(handle =>
        {
            FakeTexture texture = new(handle.Name);
            loaded.Add(texture);

            return texture;
        });

        store.ChangeScene([Hero]);
        store.ChangeScene([Tiles]);
        int resident = loaded.Count(texture => !texture.Disposed);

        for (int transition = 0; transition < 100; transition++)
        {
            store.ChangeScene([transition % 2 == 0 ? Hero : Tiles]);
        }

        Assert.Equal(1, resident);
        Assert.Equal(resident, loaded.Count(texture => !texture.Disposed));
        Assert.Equal(102, loaded.Count);
    }

    [Fact]
    public void APreloadFailure_RetainsPriorAssetsAndDisposesStagedAssets()
    {
        TextureHandle stagedHandle = new("staged", ".png");
        FakeTexture? staged = null;
        bool fail = false;
        using SceneAssetStore<TextureHandle, FakeTexture> store = SyncStore.Over<TextureHandle, FakeTexture>(handle =>
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
    public void AFailureInAnotherStore_RollsBackAllStagedAssets()
    {
        FakeTexture? staged = null;
        using SceneAssetStore<TextureHandle, FakeTexture> textures = SyncStore.Over<TextureHandle, FakeTexture>(handle =>
        {
            FakeTexture texture = new(handle.Name);
            if (handle == Tiles)
            {
                staged = texture;
            }

            return texture;
        });
        using SceneAssetStore<string, FakeTexture> sounds = SyncStore.Over<string, FakeTexture>(name =>
            name == "broken" ? throw new InvalidDataException("audio decode failed") : new FakeTexture(name));
        FakeTexture hero = textures.Get(Hero);
        FakeTexture sound = sounds.Get("current");

        Assert.Throws<InvalidDataException>(() =>
            textures.ChangeScene([Tiles], () => sounds.ChangeScene(["broken"])));

        Assert.Same(hero, textures.Get(Hero));
        Assert.Same(sound, sounds.Get("current"));
        Assert.False(hero.Disposed);
        Assert.False(sound.Disposed);
        Assert.True(staged!.Disposed);

        textures.ChangeScene([Tiles], () => sounds.ChangeScene(["next"]));

        Assert.True(hero.Disposed);
        Assert.True(sound.Disposed);
        Assert.False(textures.Get(Tiles).Disposed);
        Assert.False(sounds.Get("next").Disposed);
    }

    [Fact]
    public void Dispose_ReleasesEveryAssetOwnedByTheScene()
    {
        SceneAssetStore<TextureHandle, FakeTexture> store = SyncStore.Over<TextureHandle, FakeTexture>(
            static handle => new FakeTexture(handle.Name));
        store.ChangeScene([Hero]);
        FakeTexture preload = store.Get(Hero);
        FakeTexture lazy = store.Get(Tiles);

        store.Dispose();

        Assert.True(preload.Disposed);
        Assert.True(lazy.Disposed);
    }

    [Fact]
    public void TheTexelPool_KeepsItsLargestBuffersUpToTheDecodeConcurrency()
    {
        TexelPool pool = new();
        byte[][] pages = [.. Enumerable.Range(1, AssetDecodes.Concurrency).Select(static size => new byte[100_000 * size])];
        foreach (byte[] page in pages)
        {
            pool.Return(page);
        }

        pool.Return(new byte[84_999]);
        pool.Return(new byte[99_999]);
        byte[] largest = new byte[1_000_000];
        pool.Return(largest);

        Assert.Equal(AssetDecodes.Concurrency, pool.Count);
        Assert.Same(largest, pool.Rent(largest.Length));
        Assert.NotSame(pages[0], pool.Rent(pages[0].Length));
    }

    private sealed class Shipped : IDisposable
    {
        private readonly TempWorkspace _workspace = new("capsule-textures-");

        internal Shipped(params TextureHandle[] textures)
        {
            foreach (TextureHandle handle in textures)
            {
                Path = _workspace.PathTo(TextureFiles.RelativePathOf(handle));
                File.WriteAllBytes(Path, []);
            }
        }

        internal string BaseDirectory => _workspace.Root;

        internal ContentPlatform Platform => new(_workspace.Root);

        /// <summary>The last file shipped, which is the only one the single-handle specs ship.</summary>
        internal string Path { get; private set; } = string.Empty;

        public void Dispose() => _workspace.Dispose();
    }

    private sealed class FakeTexture(string name) : IDisposable
    {
        internal string Name => name;

        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
