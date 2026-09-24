using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// What the build will and will not read out of a sprite sheet, and what a game compiles against
/// once it has: every frame and clip is literal data by the time the game runs, and no sheet ships.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class SpriteToolTests
{
    private const string Player = """
        { "formatVersion": 1,
          "texture": "actors/player.png",
          "frames": [
            { "name": "idle-0", "x": 0, "y": 0, "width": 8, "height": 8, "pivot": [4, 8] },
            { "name": "walk-0", "x": 8, "y": 0, "width": 8, "height": 8 } ],
          "clips": [
            { "name": "idle", "loop": true, "frames": [ { "frame": "idle-0", "ticks": 6 } ] },
            { "name": "land", "frames": [ { "frame": "walk-0", "ticks": 1 }, { "frame": "idle-0", "ticks": 2 } ] } ] }
        """;

    private const string Prop = """
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 1, "y": 2, "width": 3, "height": 4 } ] }
        """;

    // A socket every frame need not set: the walk's second frame bobs it, the third leaves it out.
    private const string Armed = """
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" }, { "name": "off-hand" } ],
          "frames": [
            { "name": "walk-0", "x": 0, "y": 0, "width": 8, "height": 8, "pivot": [4, 8], "sockets": { "muzzle": [8, 4], "off-hand": [0, 5.5] } },
            { "name": "walk-1", "x": 8, "y": 0, "width": 8, "height": 8, "pivot": [4, 8], "sockets": { "muzzle": [8, 3] } },
            { "name": "walk-2", "x": 16, "y": 0, "width": 8, "height": 8, "pivot": [4, 8] } ] }
        """;

    [Fact]
    public void AFrame_CompilesIntoASpriteCarryingItsRegionPivotAndTexture()
    {
        string generated = Emitted(("actors/player", Player));

        Assert.Contains("public static class Actors", generated, StringComparison.Ordinal);
        Assert.Contains("public static class PlayerSheet", generated, StringComparison.Ordinal);
        Assert.Contains("public static global::Capsule.Rendering.Sprite Idle0 => new global::Capsule.Rendering.Sprite(", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Capsule.Assets.TextureHandle(\"actors/player\", \".png\"),", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Capsule.Rendering.TextureRegion(0, 0, 8, 8),", generated, StringComparison.Ordinal);
        Assert.Contains("new global::System.Numerics.Vector2(4F, 8F));", generated, StringComparison.Ordinal);

        // An absent pivot is the region's top-left corner, which is Sprite.Pivot's own default.
        Assert.Contains("new global::System.Numerics.Vector2(0F, 0F));", generated, StringComparison.Ordinal);
    }

    // A clip is identified by instance, so the member hands back one instance rather than rebuilding
    // it per read: a property with a field initializer, never an expression body.
    [Fact]
    public void AClip_CompilesIntoOneSpriteClipOfItsFramesTicksAndLoop()
    {
        string generated = Emitted(("actors/player", Player));

        Assert.Contains("public static global::Capsule.Animation.SpriteClip Idle { get; } = new global::Capsule.Animation.SpriteClip(", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Capsule.Rendering.Sprite[] { Frames.Idle0 },", generated, StringComparison.Ordinal);
        Assert.Contains("new int[] { 6 },", generated, StringComparison.Ordinal);
        Assert.Contains("<c>idle</c>: 1 frame, 6 ticks, looping.", generated, StringComparison.Ordinal);

        Assert.Contains("new global::Capsule.Rendering.Sprite[] { Frames.Walk0, Frames.Idle0 },", generated, StringComparison.Ordinal);
        Assert.Contains("new int[] { 1, 2 },", generated, StringComparison.Ordinal);
        Assert.Contains("<c>land</c>: 2 frames, 3 ticks, played once.", generated, StringComparison.Ordinal);
    }

    // No empty class on a sheet of frames only: a consumer naming Clips or Sockets is then a compile
    // error rather than a member that never resolves.
    [Fact]
    public void ASheetOfFramesOnly_DeclaresNoClipsOrSocketsClass()
    {
        string generated = Emitted(("prop", Prop));

        Assert.Contains("public static class Frames", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public static class Clips", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public static class Sockets", generated, StringComparison.Ordinal);
    }

    // A socket is a name constant the game binds by and a point each frame carries or leaves out; one
    // table per frame is what makes two reads of that frame equal and allocation-free.
    [Fact]
    public void ASocket_CompilesIntoANameConstantAndThePointsOfTheFramesThatSetIt()
    {
        string generated = Emitted(("armed", Armed));

        Assert.Contains("public const string Muzzle = \"muzzle\";", generated, StringComparison.Ordinal);
        Assert.Contains("public const string OffHand = \"off-hand\";", generated, StringComparison.Ordinal);
        Assert.Contains(
            "new global::Capsule.Rendering.SpriteSocket(\"muzzle\", new global::System.Numerics.Vector2(8F, 4F)),",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::Capsule.Rendering.SpriteSocket(\"off-hand\", new global::System.Numerics.Vector2(0F, 5.5F))",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("private static readonly global::Capsule.Rendering.SpriteSocket[] Walk1_Sockets", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Walk2_Sockets", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ADirectoryUnderTheSpritesRoot_BecomesANestedClass()
    {
        string generated = Emitted(("prop", Prop), ("actors/player", Player));

        Assert.Contains("Every asset authored under <c>sprites/actors</c>.", generated, StringComparison.Ordinal);
    }

    // The sheet's own spelling of the texture is normalized to the key the build ships it under, so a
    // sheet naming 'P.PNG' carries the handle of the 'p.png' that shipped.
    [Theory]
    [InlineData("p.png")]
    [InlineData("P.png")]
    [InlineData("p.PNG")]
    public void ATextureSpelledAnotherWay_ResolvesToTheShippedHandle(string texture)
    {
        string generated = Emitted(("prop", Prop.Replace("\"p.png\"", $"\"{texture}\"", StringComparison.Ordinal)));

        Assert.Contains("new global::Capsule.Assets.TextureHandle(\"p\", \".png\")", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ATextureTheGameDoesNotShip_FailsTheBuild()
    {
        string refused = Refused(("prop", Prop.Replace("\"p.png\"", "\"other.png\"", StringComparison.Ordinal)));

        Assert.Contains("Assets/Sprites/prop.sheet.json", refused, StringComparison.Ordinal);
        Assert.Contains("does not ship", refused, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoSheetsThatBecomeOneIdentifier_FailTheBuild() =>
        Assert.Contains(
            "already claims",
            Refused(("main-prop", Prop), ("main_prop", Prop)),
            StringComparison.Ordinal);

    // A sheet's class is named for its file and its type, so no sheet name is one of the classes a
    // sheet declares inside itself. One filed in a folder of its class's own name is CS0542.
    [Fact]
    public void ASheetInAFolderOfItsClassesName_FailsTheBuild() =>
        Assert.Contains("inside a generated class of that name", Refused(("prop-sheet/prop", Prop)), StringComparison.Ordinal);

    [Fact]
    public void ASheetNamedAfterItsOwnClasses_IsDeclared() =>
        Assert.Contains("public static class FramesSheet", Emitted(("frames", Prop)), StringComparison.Ordinal);

    // A null where the format declares an optional member is that member left out, so a tool writing
    // its whole schema out authors the same sheet as one that omits what it has nothing to say about.
    [Fact]
    public void ANullInAnOptionalPosition_IsThatMemberAbsent()
    {
        string generated = Emitted(
            ("prop", """
                { "formatVersion": 1, "texture": "p.png", "source": null, "clips": null, "sockets": null,
                  "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": null, "sockets": null } ] }
                """),
            ("held", """
                { "formatVersion": 1, "texture": "p.png",
                  "source": { "tool": null, "path": "p", "hash": null },
                  "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
                  "clips": [ { "name": "c", "loop": null, "frames": [ { "frame": "a", "ticks": 1 } ] } ] }
                """));

        Assert.Contains("new global::System.Numerics.Vector2(0F, 0F));", generated, StringComparison.Ordinal);
        Assert.Contains("played once", generated, StringComparison.Ordinal);
    }

    // The identifier rule is applied to the text a name's escapes decode to.
    [Fact]
    public void ANameSpelledWithEscapes_IsTheNameItDecodesTo() =>
        Assert.Contains(
            "Sprite Idle0 =>",
            Emitted(("prop", """
                { "formatVersion": 1, "texture": "p.png",
                  "frames": [ { "name": "idle-0", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
                """)),
            StringComparison.Ordinal);

    [Theory]

    // The version gate.
    [InlineData("""{ "texture": "p.png", "frames": [] }""", "formatVersion")]
    [InlineData("""{ "formatVersion": 2, "texture": "p.png", "frames": [] }""", "unsupported")]

    // A texture that is not one asset path under the textures root.
    [InlineData("""{ "formatVersion": 1, "frames": [] }""", "names no texture")]
    [InlineData("""{ "formatVersion": 1, "texture": null, "frames": [] }""", "names no texture")]
    [InlineData("""{ "formatVersion": 1, "texture": "sub//p.png", "frames": [] }""", "extension included")]
    [InlineData("""{ "formatVersion": 1, "texture": "../p.png", "frames": [] }""", "extension included")]
    [InlineData("""{ "formatVersion": 1, "texture": "player", "frames": [] }""", "extension included")]
    [InlineData("""{ "formatVersion": 1, "texture": "01/p.png", "frames": [] }""", "no C# name")]

    // A sheet names at least one region of its texture.
    [InlineData("""{ "formatVersion": 1, "texture": "p.png" }""", "has no frames")]
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", "frames": null }""", "has no frames")]
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", "frames": [] }""", "empty frames list")]
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", "frames": [ null ] }""", "frames[0] as null")]

    // Frame geometry.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 0, "height": 1 } ] }
        """, "at least one texel")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": -1, "y": 0, "width": 1, "height": 1 } ] }
        """, "not negative")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "y": 0, "width": 1, "height": 1 } ] }
        """, "no x")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": null, "y": 0, "width": 1, "height": 1 } ] }
        """, "no x")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": [1] } ] }
        """, "1 components")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": [1, 2, 3] } ] }
        """, "3 components")]

    // Names.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 },
                      { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """, "second \"a\"")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a-b", "x": 0, "y": 0, "width": 1, "height": 1 },
                      { "name": "a_b", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """, "one C# name")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a b", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """, "no C# name")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "frames", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """, "'Frames' class")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "clips", "frames": [ { "frame": "a", "ticks": 1 } ] } ] }
        """, "'Clips' class")]

    // Sockets: declared once, set by name, and set by at least one frame.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzle": [1, 1] } } ] }
        """, "does not declare")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" }, { "name": "hand" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzzle": [1, 1] } } ] }
        """, "no frame sets")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" }, { "name": "muzzle" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzzle": [1, 1] } } ] }
        """, "second \"muzzle\"")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "sockets" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "sockets": [1, 1] } } ] }
        """, "'Sockets' class")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzzle": [1] } } ] }
        """, "1 components")]

    // A JSON number too large for a float reads as an infinity, which no texel offset is.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": [1e400, 0] } ] }
        """, "pivot that is not finite")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzzle": [1e400, 1] } } ] }
        """, "not finite")]

    // Clip entries.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "b", "ticks": 1 } ] } ] }
        """, "no frame named")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "a", "ticks": 0 } ] } ] }
        """, "at least one fixed step")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [] } ] }
        """, "no frames")]
    public void ASheetTheBuildCannotRead_FailsTheBuildNamingTheDefect(string json, string defect)
    {
        string refused = Refused(("prop", json));

        Assert.Contains("Assets/Sprites/prop.sheet.json", refused, StringComparison.Ordinal);
        Assert.Contains(defect, refused, StringComparison.Ordinal);
    }

    // JSON the reader cannot get through at all, or a member the format does not declare, which is
    // what a typo looks like. The reader's own message names the member and the line.
    [Theory]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "fps": 12,
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """)]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "w": 1, "height": 1 } ] }
        """)]
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", """)]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 01, "y": 0, "width": 1, "height": 1 } ] }
        """)]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": [.5, 0] } ] }
        """)]

    public void JsonTheReaderRefuses_FailsTheBuild(string json) =>
        Assert.Contains("Assets/Sprites/prop.sheet.json", Refused(("prop", json)), StringComparison.Ordinal);

    private static string Emitted(params (string Key, string Json)[] sheets)
    {
        using ToolWorkspace workspace = Authored(sheets);
        workspace.Succeed();

        return workspace.Generated;
    }

    private static string Refused(params (string Key, string Json)[] sheets)
    {
        using ToolWorkspace workspace = Authored(sheets);

        return workspace.Fail();
    }

    // Every sheet here cuts from one of these two textures, which a build copies and never decodes.
    private static ToolWorkspace Authored((string Key, string Json)[] sheets)
    {
        ToolWorkspace workspace = new();
        workspace.Write("Assets/p.png", string.Empty);
        workspace.Write("Assets/actors/player.png", string.Empty);
        foreach ((string key, string json) in sheets)
        {
            workspace.Write($"Assets/Sprites/{key}.sheet.json", json);
        }

        return workspace;
    }
}
