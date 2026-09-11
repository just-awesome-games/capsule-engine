using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// What the compiler will and will not read out of a sprite sheet, and what a game compiles against
// once it has: every frame and clip is literal data by the time the game runs, and no sheet ships.
public sealed class SpriteGeneratorTests
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

    // The two characters a raw string literal would spell as an escape rather than carry: the tab is
    // inside the string it breaks, and the no-break space sits between two members.
    private const string RawTabInName =
        "{ \"formatVersion\": 1, \"texture\": \"p.png\", \"frames\": [ { \"name\": \"a\tb\", \"x\": 0, \"y\": 0, \"width\": 1, \"height\": 1 } ] }";

    private const string NoBreakSpaceBetweenMembers =
        "{ \"formatVersion\": 1, \"texture\": \"p.png\", \"frames\": [ { \"name\": \"a\", \"x\": 0, \"y\": 0, \"width\": 1, \"height\": 1 } ] }";

    [Fact]
    public void AFrame_CompilesIntoASpriteCarryingItsRegionPivotAndTexture()
    {
        Assembly game = Probed(
            """
            public static Capsule.Rendering.Sprite Pivoted => CapsuleAssets.Sprites.Actors.Player.Frames.Idle0;
            public static Capsule.Rendering.Sprite Plain => CapsuleAssets.Sprites.Actors.Player.Frames.Walk0;
            """,
            ("sprites/actors/player.sheet.json", Player),
            ("textures/actors/player.png", null));

        Sprite pivoted = Sprite(game, "Pivoted");

        Assert.Equal(new TextureHandle("actors/player", ".png"), pivoted.Texture);
        Assert.Equal(new TextureRegion(0, 0, 8, 8), pivoted.Region);
        Assert.Equal(new Vector2(4, 8), pivoted.Pivot);

        // An absent pivot is the region's top-left corner, which is Sprite.Pivot's own default.
        Assert.Equal(Vector2.Zero, Sprite(game, "Plain").Pivot);
    }

    // A clip is identified by instance — a game compares the clip playing against the one a sheet
    // declared — so the member hands back one instance rather than rebuilding it per read.
    [Fact]
    public void AClip_CompilesIntoOneSpriteClipOfItsFramesTicksAndLoop()
    {
        Assembly game = Probed(
            """
            public static Capsule.Animation.SpriteClip Idle => CapsuleAssets.Sprites.Actors.Player.Clips.Idle;
            public static Capsule.Animation.SpriteClip Land => CapsuleAssets.Sprites.Actors.Player.Clips.Land;
            public static bool OneInstance =>
                ReferenceEquals(CapsuleAssets.Sprites.Actors.Player.Clips.Idle, CapsuleAssets.Sprites.Actors.Player.Clips.Idle);
            """,
            ("sprites/actors/player.sheet.json", Player),
            ("textures/actors/player.png", null));

        SpriteClip idle = Clip(game, "Idle");

        Assert.True(idle.Loop);
        Assert.Equal([6], idle.FrameTicks.ToArray());
        Assert.Equal(new TextureRegion(0, 0, 8, 8), idle.Frames[0].Region);

        SpriteClip land = Clip(game, "Land");

        Assert.False(land.Loop);
        Assert.Equal([1, 2], land.FrameTicks.ToArray());
        Assert.Equal([new TextureRegion(8, 0, 8, 8), new TextureRegion(0, 0, 8, 8)], Regions(land));

        Assert.True((bool)Read(game, "OneInstance"));
    }

    // No empty class on a sheet of frames only: a consumer naming Clips is then a compile error
    // rather than a member that never resolves.
    [Fact]
    public void ASheetOfFramesOnly_DeclaresNoClipsClass()
    {
        Type sheet = Sheet(Compiled(("sprites/prop.sheet.json", Prop), ("textures/p.png", null)), "Prop");

        Assert.NotNull(sheet.GetNestedType("Frames"));
        Assert.Null(sheet.GetNestedType("Clips"));
    }

    [Fact]
    public void ADirectoryUnderTheSpritesRoot_BecomesANestedClass()
    {
        Assembly game = Compiled(
            ("sprites/prop.sheet.json", Prop),
            ("sprites/actors/player.sheet.json", Player),
            ("textures/p.png", null),
            ("textures/actors/player.png", null));

        Assert.NotNull(Sheet(game, "Prop"));
        Assert.NotNull(Sheet(game, "Actors", "Player"));
    }

    // A sheet cutting from a texture the build does not ship would carry a handle that finds no file
    // at run time; the shipped spelling is ordinal, so a case variant is a different texture.
    [Theory]
    [InlineData("p.png")]
    [InlineData("P.png")]
    [InlineData("p.PNG")]
    public void ATextureTheGameDoesNotShip_FailsTheBuild(string texture)
    {
        Diagnostic refused = Assert.Single(Refused(
            ("sprites/prop.sheet.json", Prop.Replace("\"p.png\"", $"\"{texture}\"", StringComparison.Ordinal)),
            ("textures/other.png", null)));

        Assert.Equal("CAP025", refused.Id);
        Assert.Contains("does not ship", refused.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("sprites/prop.sheet.json", refused.Location.GetLineSpan().Path);
    }

    [Fact]
    public void TwoSheetsThatBecomeOneIdentifier_FailTheBuild()
    {
        Assert.Equal(
            "CAP016",
            Assert.Single(Refused(
                ("sprites/main-prop.sheet.json", Prop),
                ("sprites/main_prop.sheet.json", Prop),
                ("textures/p.png", null))).Id);
    }

    // 'Frames' and 'Clips' are the classes a sheet declares inside itself, so a sheet of that name
    // would declare a class inside a class of the same name (CS0542).
    [Theory]
    [InlineData("sprites/frames.sheet.json", "CAP018")]
    [InlineData("sprites/clips.sheet.json", "CAP018")]
    [InlineData("sprites/sprites.sheet.json", "CAP018")]
    [InlineData("sprites/01-prop.sheet.json", "CAP017")]
    public void AKeyTheGeneratedClassesCannotDeclare_FailsTheBuild(string sheet, string diagnostic) =>
        Assert.Equal(diagnostic, Assert.Single(Refused((sheet, Prop), ("textures/p.png", null))).Id);

    // A directory takes neither name: only a leaf declares the two classes.
    [Fact]
    public void ADirectoryNamedAfterASheetsOwnClasses_IsDeclared() =>
        Assert.NotNull(Sheet(
            Compiled(("sprites/frames/prop.sheet.json", Prop), ("textures/p.png", null)),
            "Frames",
            "Prop"));

    // A null where the format declares an optional member is that member left out, so a tool writing
    // its whole schema out authors the same sheet as one that omits what it has nothing to say about.
    [Fact]
    public void ANullInAnOptionalPosition_IsThatMemberAbsent()
    {
        Assembly game = Probed(
            """
            public static Capsule.Rendering.Sprite Plain => CapsuleAssets.Sprites.Prop.Frames.A;
            public static Capsule.Animation.SpriteClip Once => CapsuleAssets.Sprites.Held.Clips.C;
            """,
            ("sprites/prop.sheet.json", """
                { "formatVersion": 1, "texture": "p.png", "source": null, "clips": null,
                  "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": null } ] }
                """),
            ("sprites/held.sheet.json", """
                { "formatVersion": 1, "texture": "p.png",
                  "source": { "tool": null, "path": "p", "hash": null },
                  "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
                  "clips": [ { "name": "c", "loop": null, "frames": [ { "frame": "a", "ticks": 1 } ] } ] }
                """),
            ("textures/p.png", null));

        Assert.Equal(Vector2.Zero, Sprite(game, "Plain").Pivot);
        Assert.False(Clip(game, "Once").Loop);
        Assert.Null(Sheet(game, "Prop").GetNestedType("Clips"));
    }

    // A name is the text its escapes decode to, which is what the identifier rule is applied to.
    [Fact]
    public void ANameSpelledWithEscapes_IsTheNameItDecodesTo() =>
        Assert.NotNull(Sheet(
                Compiled(
                    ("sprites/prop.sheet.json", """
                        { "formatVersion": 1, "texture": "p.png",
                          "frames": [ { "name": "\u0069dle\u002d0", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
                        """),
                    ("textures/p.png", null)),
                "Prop")
            .GetNestedType("Frames")!
            .GetProperty("Idle0"));

    [Theory]

    // The version gate.
    [InlineData("""{ "texture": "p.png", "frames": [] }""", "formatVersion")]
    [InlineData("""{ "formatVersion": 2, "texture": "p.png", "frames": [] }""", "unsupported")]

    // A texture that is not one asset path under the textures root.
    [InlineData("""{ "formatVersion": 1, "frames": [] }""", "names no texture")]
    [InlineData("""{ "formatVersion": 1, "texture": "sub//p.png", "frames": [] }""", "extension included")]
    [InlineData("""{ "formatVersion": 1, "texture": "../p.png", "frames": [] }""", "extension included")]
    [InlineData("""{ "formatVersion": 1, "texture": "player", "frames": [] }""", "extension included")]

    // A sheet names at least one region of its texture.
    [InlineData("""{ "formatVersion": 1, "texture": "p.png" }""", "has no frames")]
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", "frames": [] }""", "empty frames list")]

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

    // A member the format does not declare, which is what a typo looks like.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "fps": 12,
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """, "malformed")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "w": 1, "height": 1 } ] }
        """, "malformed")]

    // JSON the reader cannot get through at all.
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", """, "expected")]
    [InlineData("not json", "expected")]
    [InlineData("", "is empty")]

    // A null where the format declares a required member is that member missing.
    [InlineData("""{ "formatVersion": 1, "texture": null, "frames": [] }""", "expected")]
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", "frames": null }""", "expected")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": null, "y": 0, "width": 1, "height": 1 } ] }
        """, "expected")]
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", "frames": [ null ] }""", "expected")]

    // The number grammar: a sign, a leading zero and a bare point are all a number is not.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": +0, "y": 0, "width": 1, "height": 1 } ] }
        """, "expected")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 01, "y": 0, "width": 1, "height": 1 } ] }
        """, "expected")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": [.5, 0] } ] }
        """, "expected")]

    // A raw control character belongs in an escape, and only JSON's own four characters are space.
    [InlineData(RawTabInName, "control character")]
    [InlineData(NoBreakSpaceBetweenMembers, "expected")]
    public void ASheetTheBuildCannotRead_FailsTheBuildNamingTheDefect(string json, string defect)
    {
        Diagnostic refused = Assert.Single(Refused(("sprites/prop.sheet.json", json), ("textures/p.png", null)));

        Assert.Equal("CAP024", refused.Id);
        Assert.Contains(defect, refused.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("sprites/prop.sheet.json", refused.Location.GetLineSpan().Path);
    }

    // A defect one line carries is anchored to that line, so the build error navigates to it.
    [Fact]
    public void ADefectOnOneLine_IsAnchoredToThatLine()
    {
        Diagnostic refused = Assert.Single(Refused(
            ("sprites/prop.sheet.json", """
                { "formatVersion": 1,
                  "texture": "p.png",
                  "frames": [
                    { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 },
                    { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
                """),
            ("textures/p.png", null)));

        Assert.Equal(4, refused.Location.GetLineSpan().StartLinePosition.Line);
    }

    // The compiler reads text; a sheet it cannot decode is named rather than silently skipped.
    [Fact]
    public void ASheetTheCompilerCannotRead_FailsTheBuild() =>
        Assert.Equal("CAP024", Assert.Single(Refused(("sprites/prop.sheet.json", null))).Id);

    // Nothing ships for a sheet, so a game that authors none still compiles against the class.
    [Fact]
    public void AGameAuthoringNoSheet_StillDeclaresTheDomain() =>
        Assert.NotNull(Compiled(("textures/p.png", null))
            .GetType("Capsule.Assets.Generated.CapsuleAssets")!
            .GetNestedType("Sprites"));

    private static Assembly Compiled(params (string Path, string? Content)[] assets) =>
        Probed(string.Empty, assets);

    private static Assembly Probed(string members, params (string Path, string? Content)[] assets)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            "using Capsule.Assets.Generated;\n\nnamespace Game;\n\npublic static class Probe\n{\n" + members + "\n}\n",
            logic: true,
            assets);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        return GeneratorHarness.Loaded(compiled);
    }

    private static IEnumerable<Diagnostic> Refused(params (string Path, string? Content)[] assets) =>
        GeneratorHarness.Errors(GeneratorHarness.CompileWithSources(logic: true, assets).Diagnostics);

    private static object Read(Assembly game, string member) =>
        game.GetType("Game.Probe")!.GetProperty(member)!.GetValue(null)!;

    private static Sprite Sprite(Assembly game, string member) => (Sprite)Read(game, member);

    private static SpriteClip Clip(Assembly game, string member) => (SpriteClip)Read(game, member);

    private static TextureRegion[] Regions(SpriteClip clip)
    {
        TextureRegion[] regions = new TextureRegion[clip.Frames.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            regions[i] = clip.Frames[i].Region;
        }

        return regions;
    }

    // CapsuleAssets.Sprites.<path>, as the game names it.
    private static Type Sheet(Assembly game, params string[] path)
    {
        Type declaring = game.GetType("Capsule.Assets.Generated.CapsuleAssets")!.GetNestedType("Sprites")!;

        foreach (string segment in path)
        {
            declaring = declaring.GetNestedType(segment)!;
        }

        return declaring;
    }
}
