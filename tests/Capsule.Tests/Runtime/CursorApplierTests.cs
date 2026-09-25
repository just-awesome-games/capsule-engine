using System.Numerics;
using Capsule.Assets;
using Capsule.Build.Atlases;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Input;

namespace Capsule.Tests.Runtime;

public sealed class CursorApplierTests
{
    private static readonly Sprite Crosshair = new(new TextureHandle("crosshair", ".png"), new TextureRegion(0, 0, 9, 9), new Vector2(4f, 4f));

    [Fact]
    public void TheWindowShows_TheGamesCursorUnlessThePadHidesItOrTheOverlayPointsWithTheArrow()
    {
        Cursor cursor = new() { Image = Crosshair, Confined = true };

        Assert.Equal(new CursorLook(false, Crosshair, true, 2), CursorApplier.Resolve(cursor, padActive: true, overlayOpen: false, layerScale: 2f));
        Assert.Equal(new CursorLook(true, null, false, 0), CursorApplier.Resolve(cursor, padActive: true, overlayOpen: true, layerScale: 2f));

        cursor.Visible = false;

        Assert.Equal(new CursorLook(false, Crosshair, true, 2), CursorApplier.Resolve(cursor, padActive: false, overlayOpen: false, layerScale: 2f));
    }

    [Theory]
    [InlineData(2.4f, 9, 2)]
    [InlineData(2.5f, 9, 3)]
    [InlineData(0.3f, 9, 1)]
    [InlineData(4f, 100, 2)]
    [InlineData(3f, 300, 1)]
    public void TheImageScale_IsTheLayersRoundedAtLeastOneAndKeepsASideWithin256Pixels(float layerScale, int side, int factor) =>
        Assert.Equal(factor, CursorApplier.ScaleFactor(layerScale, new TextureRegion(0, 0, side, side / 2)));

    [Fact]
    public void APackedImage_IsReadFromItsPageAtTheOffsetInStraightAlpha()
    {
        using TempWorkspace workspace = new("cursor-texels");
        byte[] page = new byte[4 * 3 * 4];
        for (int i = 0; i < page.Length; i += 4)
        {
            page[i] = 255;
            page[i + 3] = 255;
        }

        byte[] wanted = [200, 100, 50, 51, 10, 20, 30, 255];
        wanted.CopyTo(page, ((1 * 4) + 2) * 4);
        using (FileStream png = File.Create(workspace.PathTo("assets/game.0.png")))
        {
            AtlasStep.Encode(page, 4, 3, png);
        }

        File.WriteAllText(workspace.PathTo("assets/atlases.json"), """{ "textures": { "crosshair": { "page": "game.0", "x": 1, "y": 1 } } }""");
        ContentPlatform platform = new(workspace.Root);

        byte[] texels = TextureStore.ReadRegion(platform, AtlasMap.Load(platform), Crosshair.Texture, new TextureRegion(1, 0, 2, 1));

        Assert.Equal(wanted, texels);
    }
}
