using System.Numerics;
using System.Reflection;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Build;

/// <summary>
/// What the build will and will not read out of a variant sheet: another texture's drawing of every
/// frame of one base sheet, and nothing the base plays, raises or places.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class SheetVariantToolTests
{
    // Player's other look: idle-0 drawn from p.png, walk-0 drawn as nothing.
    private const string Hat = """
        { "formatVersion": 1, "texture": "p.png", "variantOf": "Sprites/actors/player.sheet.json",
          "frames": [
            { "name": "idle-0", "x": 0, "y": 0, "width": 6, "height": 3, "pivot": [3, 10] },
            { "name": "walk-0" } ] }
        """;

    // A frame giving only its name is empty. It maps its base frame to nothing and is no sprite of
    // the variant's own.
    [Fact]
    public void AVariant_CompilesIntoItsFramesAndAVariantOfEveryBaseFrame()
    {
        Assembly game = SheetToolTests.Loaded(SheetToolTests.Emitted(("actors/player", SheetToolTests.Player), ("actors/player-hat", Hat)));
        SpriteVariant variant = SheetToolTests.Member<SpriteVariant>(game, "Actors+PlayerHatSheet", "Variant");
        Sprite hat = SheetToolTests.Member<Sprite>(game, "Actors+PlayerHatSheet+Frames", "Idle0");

        Assert.Equal(new Sprite(new TextureHandle("p", ".png"), new TextureRegion(0, 0, 6, 3), new Vector2(3f, 10f)), hat);
        Assert.Null(game.GetType("Capsule.Generated.CapsuleAssets+Sprites+Actors+PlayerHatSheet+Frames")!.GetProperty("Walk0"));
        Assert.True(variant.TryGet(SheetToolTests.Member<Sprite>(game, "Actors+PlayerSheet+Frames", "Idle0"), out Sprite? idle));
        Assert.Equal(hat, idle);
        Assert.True(variant.TryGet(SheetToolTests.Member<Sprite>(game, "Actors+PlayerSheet+Frames", "Walk0"), out Sprite? walk));
        Assert.Null(walk);
    }

    // A variant whose every frame draws nothing declares no frames and maps every base frame to nothing.
    [Fact]
    public void AVariantDrawingNothing_MapsEveryBaseFrameToNothing()
    {
        const string bare = """
            { "formatVersion": 1, "texture": "p.png", "variantOf": "Sprites/actors/player.sheet.json",
              "frames": [ { "name": "idle-0" }, { "name": "walk-0" } ] }
            """;
        Assembly game = SheetToolTests.Loaded(SheetToolTests.Emitted(("actors/player", SheetToolTests.Player), ("actors/player-bare", bare)));
        SpriteVariant variant = SheetToolTests.Member<SpriteVariant>(game, "Actors+PlayerBareSheet", "Variant");

        Assert.Null(game.GetType("Capsule.Generated.CapsuleAssets+Sprites+Actors+PlayerBareSheet+Frames"));
        Assert.True(variant.TryGet(SheetToolTests.Member<Sprite>(game, "Actors+PlayerSheet+Frames", "Idle0"), out Sprite? idle));
        Assert.Null(idle);
    }

    [Theory]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "variantOf": "Sprites/actors/player.sheet.json",
          "frames": [ { "name": "idle-0" } ] }
        """, "has no frame \"walk-0\". A variant lists every frame of its base once. Add \"walk-0\"")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "variantOf": "Sprites/actors/player.sheet.json",
          "frames": [ { "name": "idle-0" }, { "name": "walk-0" }, { "name": "run-0" } ] }
        """, "which has no frame \"run-0\". A variant lists only its base's frames. Remove \"run-0\"")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "variantOf": "Sprites/actors/player.sheet.json",
          "frames": [ { "name": "idle-0", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzzle": [1, 1] } }, { "name": "walk-0" } ] }
        """, "has frame \"idle-0\" setting a socket, and is a variant")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "variantOf": "Sprites/actors/player.sheet.json",
          "frames": [ { "name": "idle-0" }, { "name": "walk-0" } ],
          "clips": [ { "name": "idle", "frames": [ { "frame": "idle-0", "ticks": 1 } ] } ] }
        """, "declares \"clips\" and is a variant")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "variantOf": "Sprites/actors/player-hat.sheet.json",
          "frames": [ { "name": "idle-0" }, { "name": "walk-0" } ] }
        """, "which is itself a variant of \"sprites/actors/player.sheet.json\"")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "variantOf": "Sprites/actors/nobody.sheet.json",
          "frames": [ { "name": "idle-0" } ] }
        """, "which this game does not ship")]
    public void AVariantTheBuildCannotDraw_FailsTheBuildNamingTheDefect(string json, string defect)
    {
        string refused = SheetToolTests.Refused(("actors/player", SheetToolTests.Player), ("actors/player-hat", Hat), ("prop", json));

        Assert.Contains("Assets/Sprites/prop.sheet.json", refused, StringComparison.Ordinal);
        Assert.Contains(defect, refused, StringComparison.Ordinal);
    }

    // A variant finds a frame by its sprite. Two base frames that are one sprite cannot draw two ways.
    [Fact]
    public void AVariantDrawingOneSpriteTwoWays_FailsTheBuild()
    {
        string twins = """
            { "formatVersion": 1, "texture": "p.png",
              "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 }, { "name": "b", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
            """;
        string variant = """
            { "formatVersion": 1, "texture": "p.png", "variantOf": "Sprites/twins.sheet.json",
              "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 }, { "name": "b" } ] }
            """;

        Assert.Contains("are one sprite", SheetToolTests.Refused(("twins", twins), ("prop", variant)), StringComparison.Ordinal);
    }
}
