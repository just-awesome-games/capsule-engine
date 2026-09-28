using System.Text;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Runtime.Assets;

namespace Capsule.Tests.Runtime;

// How a packed handle reaches its page: the map sends it to the page and the offset of its texel
// (0, 0), leaves an unpacked handle alone, and turns a scene's preload set into the pages the store
// must hold — so a page loads once for any member and leaves only when no member wants it.
public sealed class TextureMapTests
{
    private const string Map = """
        { "textures": {
            "actors/hero": { "page": "atlases/game.0", "x": 1, "y": 1 },
            "tiles": { "page": "atlases/game.0", "x": 40, "y": 1 },
            "far/away": { "page": "atlases/game.1", "x": 1, "y": 1 },
            "glow": { "format": "r8" } },
          "pages": { "atlases/game.1": { "sampling": "point" } } }
        """;

    private static readonly TextureHandle Hero = new("actors/hero", ".png");

    private static readonly TextureHandle Tiles = new("tiles", ".png");

    private static readonly TextureHandle Away = new("far/away", ".png");

    private static readonly TextureHandle Loose = new("loose", ".png");

    private static readonly TextureHandle Page0 = new("atlases/game.0", ".png");

    // A packed handle resolves to its page, and a file's settings are read from the page or the
    // unpacked texture that carries them.
    [Fact]
    public void AMappedHandle_ResolvesToItsPageAndAFileToItsSettings()
    {
        TextureMap map = Parse(Map);

        Assert.True(map.TryGet(Tiles, out AtlasSlot tiles));
        Assert.Equal(new AtlasSlot(Page0, 40, 1), tiles);
        Assert.False(map.TryGet(Loose, out _));
        Assert.Equal(new TextureFacts(true, null), map.Facts(new TextureHandle("glow", ".png")));
        Assert.Equal(new TextureFacts(false, TextureSampling.Point), map.Facts(new TextureHandle("atlases/game.1", ".png")));
        Assert.Equal(default, map.Facts(Page0));
    }

    [Fact]
    public void TwoMembersOfOnePage_LoadItOnceAndReleaseItOnlyWhenBothLeave()
    {
        TextureMap map = Parse(Map);
        int loads = 0;
        using SceneAssetStore<TextureHandle, FakePage> store = SyncStore.Over<TextureHandle, FakePage>(handle =>
        {
            loads++;
            return new FakePage();
        });
        TextureHandle[] preloads = [Hero, Loose, Tiles];
        Assert.Equal([Page0, Loose, Page0], map.Residency(preloads));
        Assert.Same(preloads, TextureMap.Empty.Residency(preloads));

        store.ChangeScene(map.Residency([Hero, Tiles]));
        FakePage page = store.Get(Page0);

        Assert.Equal(1, loads);

        store.ChangeScene(map.Residency([Tiles, Away]));

        Assert.False(page.Disposed);
        Assert.Equal(2, loads);

        store.ChangeScene(map.Residency([Away]));

        Assert.True(page.Disposed);
    }

    [Fact]
    public void Load_ReturnsTheEmptyMapWhenNoneShipped()
    {
        using TempWorkspace workspace = new("capsule-atlas-");

        Assert.Same(TextureMap.Empty, TextureMap.Load(new ContentPlatform(workspace.Root)));
    }

    [Theory]
    [InlineData("""{ "textures": { "hero": { "x": 1, "y": 1 } } }""")]
    [InlineData("""{ "textures": { "hero": { "sampling": "bilinear" } } }""")]
    [InlineData("not json")]
    public void Parse_RefusesAMapTheBuildDidNotWrite(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    private static TextureMap Parse(string json)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        return TextureMap.Parse(stream, "textures.json");
    }

    private sealed class FakePage : IDisposable
    {
        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
