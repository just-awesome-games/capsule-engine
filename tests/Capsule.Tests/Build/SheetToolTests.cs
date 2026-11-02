using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Capsule.Tests.Build;

/// <summary>
/// What a game compiles against once the build has read a sprite sheet. Every frame and clip is
/// constant data by the time the game runs, and no sheet ships.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class SheetToolTests
{
    internal const string Player = """
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
    // The first frame's box reaches past its region, and only the walk's first entry raises an event.
    private const string Armed = """
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" }, { "name": "off-hand" } ],
          "boxes": [ { "name": "hurt" } ],
          "events": [ { "name": "footstep" } ],
          "frames": [
            { "name": "walk-0", "x": 0, "y": 0, "width": 8, "height": 8, "pivot": [4, 8], "sockets": { "muzzle": [8, 4], "off-hand": [0, 5.5] },
              "boxes": { "hurt": { "x": -1, "y": 0.5, "width": 10, "height": 7 } } },
            { "name": "walk-1", "x": 8, "y": 0, "width": 8, "height": 8, "pivot": [4, 8], "sockets": { "muzzle": [8, 3] } },
            { "name": "walk-2", "x": 16, "y": 0, "width": 8, "height": 8, "pivot": [4, 8] } ],
          "clips": [ { "name": "walk", "loop": true, "frames": [
            { "frame": "walk-0", "ticks": 6, "events": ["footstep"] }, { "frame": "walk-1", "ticks": 6 } ] } ] }
        """;

    // A frame's member reads its sheet's table. A sheet compiles to data, not to code per frame.
    // An absent pivot is the region's top-left corner, which is Sprite.Pivot's own default.
    [Fact]
    public void AFrame_IsASpriteCarryingItsRegionPivotAndTexture()
    {
        string generated = Emitted(("actors/player", Player));
        Assembly game = Loaded(generated);
        TextureHandle texture = new("actors/player", ".png");

        Assert.Contains("public static global::Capsule.Rendering.Sprite Idle0 => Frames_Cut[0];", generated, StringComparison.Ordinal);
        Assert.Equal(new Sprite(texture, new TextureRegion(0, 0, 8, 8), new Vector2(4f, 8f)), Member<Sprite>(game, "Actors+PlayerSheet+Frames", "Idle0"));
        Assert.Equal(new Sprite(texture, new TextureRegion(8, 0, 8, 8)), Member<Sprite>(game, "Actors+PlayerSheet+Frames", "Walk0"));
    }

    // A clip is identified by instance. Every read of its member hands back the same one.
    [Fact]
    public void AClip_IsOneSpriteClipOfItsFramesTicksAndLoop()
    {
        string generated = Emitted(("actors/player", Player));
        Assembly game = Loaded(generated);
        Sprite idle = Member<Sprite>(game, "Actors+PlayerSheet+Frames", "Idle0");
        Sprite walk = Member<Sprite>(game, "Actors+PlayerSheet+Frames", "Walk0");
        SpriteClip looped = Member<SpriteClip>(game, "Actors+PlayerSheet+Clips", "Idle");
        SpriteClip once = Member<SpriteClip>(game, "Actors+PlayerSheet+Clips", "Land");

        Assert.Same(looped, Member<SpriteClip>(game, "Actors+PlayerSheet+Clips", "Idle"));
        Assert.Equal([idle], looped.Frames.ToArray());
        Assert.Equal([6], looped.FrameTicks.ToArray());
        Assert.True(looped.Loop);
        Assert.Equal([walk, idle], once.Frames.ToArray());
        Assert.Equal([1, 2], once.FrameTicks.ToArray());
        Assert.False(once.Loop);
        Assert.Contains("<c>idle</c>: 1 frame, 6 ticks, looping.", generated, StringComparison.Ordinal);
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

    // A socket or a box is a name constant the game binds by and a mark each frame carries or leaves
    // out. One marks instance per frame is what makes two reads of that frame equal and
    // allocation-free. An event is a name constant and a list on each clip entry, never on the frame
    // it plays.
    [Fact]
    public void SocketsBoxesAndEvents_CompileIntoNameConstantsAndTheMarksAndEntriesThatSetThem()
    {
        string generated = Emitted(("armed", Armed));
        Assembly game = Loaded(generated);
        Sprite first = Member<Sprite>(game, "ArmedSheet+Frames", "Walk0");
        Sprite second = Member<Sprite>(game, "ArmedSheet+Frames", "Walk1");
        SpriteClip walk = Member<SpriteClip>(game, "ArmedSheet+Clips", "Walk");

        Assert.Equal("muzzle", Member<string>(game, "ArmedSheet+Sockets", "Muzzle"));
        Assert.Equal("off-hand", Member<string>(game, "ArmedSheet+Sockets", "OffHand"));
        Assert.Equal("hurt", Member<string>(game, "ArmedSheet+Boxes", "Hurt"));
        Assert.Equal("footstep", Member<string>(game, "ArmedSheet+Events", "Footstep"));
        Assert.Equal(
            [new SpriteSocket("muzzle", new Vector2(8f, 4f)), new SpriteSocket("off-hand", new Vector2(0f, 5.5f))],
            first.Marks!.Sockets.ToArray());
        Assert.Equal([new SpriteBox("hurt", new Rect(-1f, 0.5f, 10f, 7f))], first.Marks.Boxes.ToArray());
        Assert.Equal([new SpriteSocket("muzzle", new Vector2(8f, 3f))], second.Marks!.Sockets.ToArray());
        Assert.Empty(second.Marks.Boxes.ToArray());
        Assert.Same(second.Marks, Member<Sprite>(game, "ArmedSheet+Frames", "Walk1").Marks);
        Assert.Null(Member<Sprite>(game, "ArmedSheet+Frames", "Walk2").Marks);
        Assert.Equal(["footstep"], walk.EventsAt(0).ToArray());
        Assert.Empty(walk.EventsAt(1).ToArray());
        Assert.Contains("<c>walk-0</c>: 8x8 at (0, 0), sockets <c>muzzle</c>, <c>off-hand</c>, box <c>hurt</c>.", generated, StringComparison.Ordinal);
    }

    // The sheet's own spelling of the texture is normalized to the key the build ships it under. A
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
    public void ASheetNamedAfterItsOwnClasses_IsDeclared() =>
        Assert.Contains("public static class FramesSheet", Emitted(("frames", Prop)), StringComparison.Ordinal);

    // A null where the format declares an optional member is that member left out. A tool writing
    // its whole schema out authors the same sheet as one that omits what it has nothing to say about.
    [Fact]
    public void ANullInAnOptionalPosition_IsThatMemberAbsent()
    {
        string generated = Emitted(
            ("prop", """
                { "formatVersion": 1, "texture": "p.png", "source": null, "clips": null, "sockets": null, "boxes": null, "events": null,
                  "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": null, "sockets": null, "boxes": null } ] }
                """),
            ("held", """
                { "formatVersion": 1, "texture": "p.png",
                  "source": { "tool": null, "path": "p", "hash": null },
                  "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
                  "clips": [ { "name": "c", "loop": null, "frames": [ { "frame": "a", "ticks": 1, "events": null } ] } ] }
                """));

        Assembly game = Loaded(generated);

        Assert.Equal(Vector2.Zero, Member<Sprite>(game, "PropSheet+Frames", "A").Pivot);
        Assert.Null(Member<Sprite>(game, "PropSheet+Frames", "A").Marks);
        Assert.False(Member<SpriteClip>(game, "HeldSheet+Clips", "C").Loop);
    }

    // The identifier rule is applied to the text a name's escapes decode to.
    [Fact]
    public void ANameSpelledWithEscapes_IsTheNameItDecodesTo() =>
        Assert.Contains(
            "Sprite Idle0 =>",
            Emitted(("prop", """
                { "formatVersion": 1, "texture": "p.png",
                  "frames": [ { "name": "\u0069dle\u002D0", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
                """)),
            StringComparison.Ordinal);

    private static readonly ImmutableArray<MetadataReference> References =
        GeneratorHarness.Referenced(null, typeof(Sprite).Assembly, typeof(SceneKey).Assembly, typeof(object).Assembly);

    // The generated members as the game runs them.
    internal static Assembly Loaded(string generated) =>
        GeneratorHarness.Loaded(CSharpCompilation.Create(
            "SheetSpecs",
            [CSharpSyntaxTree.ParseText(generated)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));

    // The value of a sheet member, named by its classes under CapsuleAssets.Sprites, such as "PropSheet+Frames".
    internal static T Member<T>(Assembly game, string classes, string name)
    {
        Type declaring = game.GetType("Capsule.Generated.CapsuleAssets+Sprites+" + classes, throwOnError: true)!;

        return (T)(declaring.GetProperty(name)?.GetValue(null) ?? declaring.GetField(name)!.GetValue(null))!;
    }

    internal static string Emitted(params (string Key, string Json)[] sheets)
    {
        using ToolWorkspace workspace = Authored(sheets);
        workspace.Succeed();

        return workspace.Generated;
    }

    internal static string Refused(params (string Key, string Json)[] sheets)
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
