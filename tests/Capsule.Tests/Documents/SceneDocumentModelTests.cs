using Capsule.Assets;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;
using Capsule.Tiles;
using static Capsule.Tests.Documents.SceneDocumentFixtures;

namespace Capsule.Tests.Documents;

public sealed class SceneDocumentModelTests
{
    // The reader splits a written name on its last dot, so a handle has a written form only when
    // that split hands it back unchanged. Each of these would come back as some other handle.
    [Theory]
    [InlineData("terrain.png", "")]
    [InlineData("a", ".b.png")]
    [InlineData("", ".png")]
    [InlineData("a", "png")]
    public void ToJson_RefusesATextureHandleTheWrittenNameWouldNotSplitBackInto(string name, string extension)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.ToJson(Drawing(new TextureHandle(name, extension))));

        Assert.Contains("does not split back out of one texture path", error.Message, StringComparison.Ordinal);
    }

    // A struct's default has null parts, which is a handle with no written form, not a crash.
    [Fact]
    public void ToJson_RefusesTheDefaultTextureHandle()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.ToJson(Drawing(default)));

        Assert.Contains("does not split back out of one texture path", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(0f, float.PositiveInfinity)]
    public void Constructor_RejectsANonFiniteEntityPosition(float x, float y)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new SceneDocument([Terrain(), new EntityPlacement(2, "coin", x, y)], 3));

        Assert.Contains("not a position", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsAnEntityPlacementClaimingTheReservedTileMapType()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new SceneDocument([new EntityPlacement(1, SceneDocument.TileMapType, 0f, 0f)], 2));

        Assert.Contains("reserved", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "room.map", Sha256)]
    [InlineData("editor", "", Sha256)]
    [InlineData("editor", "room.map", "")]
    public void Constructor_RejectsAnIncompleteSourceBlock(string tool, string path, string hash)
    {
        Assert.Throws<ArgumentException>(
            () => new SceneDocument([Terrain()], 2, new SceneDocumentSource(tool, path, hash)));
    }

    [Theory]
    [InlineData("scenes\\room.map")]
    [InlineData("/scenes/room.map")]
    [InlineData("C:/scenes/room.map")]
    public void Constructor_RejectsASourcePathThatIsNotRelativeAndPortable(string path)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new SceneDocument([Terrain()], 2, new SceneDocumentSource("editor", path, Sha256)));

        Assert.Contains("must be relative", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("C304030D3D53C9C440CD5D251080080A16B34BE3832AD1218B2B63CAE622CF6D")]
    public void Constructor_RejectsAHashThatIsNotALowercaseSha256(string hash)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new SceneDocument([Terrain()], 2, new SceneDocumentSource("editor", "room.map", hash)));

        Assert.Contains("64 lowercase hex", error.Message, StringComparison.Ordinal);
    }
}
