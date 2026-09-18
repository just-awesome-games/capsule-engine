using System.Text;
using Capsule.Assets;
using Capsule.Runtime.Assets;

namespace Capsule.Tests.Runtime;

// How a packed handle reaches its page: the map sends it to the page and the offset of its texel
// (0, 0), leaves an unpacked handle alone, and turns a scene's preload set into the pages the store
// must hold — so a page loads once for any member and leaves only when no member wants it.
public sealed class AtlasMapTests
{
    private const string Map = """
        { "textures": {
            "actors/hero": { "page": "game.0", "x": 1, "y": 1 },
            "tiles": { "page": "game.0", "x": 40, "y": 1 },
            "far/away": { "page": "game.1", "x": 1, "y": 1 } } }
        """;

    private static readonly TextureHandle Hero = new("actors/hero", ".png");

    private static readonly TextureHandle Tiles = new("tiles", ".png");

    private static readonly TextureHandle Away = new("far/away", ".png");

    private static readonly TextureHandle Loose = new("loose", ".png");

    private static readonly TextureHandle Page0 = new("game.0", ".png");

    [Fact]
    public void AMappedHandle_ResolvesToItsPageAndOffsetAndAnUnmappedOneToNothing()
    {
        AtlasMap map = Parse(Map);

        Assert.True(map.TryGet(Tiles, out AtlasSlot tiles));
        Assert.Equal(new AtlasSlot(Page0, 40, 1), tiles);
        Assert.False(map.TryGet(Loose, out _));
        Assert.False(map.TryGet(TextureHandle.FontPage("actors/hero", ".png"), out _));
    }

    [Fact]
    public void TwoMembersOfOnePage_LoadItOnceAndReleaseItOnlyWhenBothLeave()
    {
        AtlasMap map = Parse(Map);
        int loads = 0;
        using SceneAssetStore<TextureHandle, FakePage> store = new(handle =>
        {
            loads++;
            return new FakePage();
        });
        TextureHandle[] preloads = [Hero, Loose, Tiles];
        Assert.Equal([Page0, Loose, Page0], map.Residency(preloads));
        Assert.Same(preloads, AtlasMap.Empty.Residency(preloads));

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

        Assert.Same(AtlasMap.Empty, AtlasMap.Load(new ContentPlatform(workspace.Root)));
    }

    [Theory]
    [InlineData("""{ "textures": { "hero": { "x": 1, "y": 1 } } }""")]
    [InlineData("not json")]
    public void Parse_RefusesAMapTheBuildDidNotWrite(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    private static AtlasMap Parse(string json)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        return AtlasMap.Parse(stream, "atlases.json");
    }

    private sealed class FakePage : IDisposable
    {
        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
