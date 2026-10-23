namespace MinimalGame.Game.Entities;

/// <summary>
/// The dusk backdrop, laid out on the screen and drawn under everything in the world.
/// </summary>
public sealed class Sky : ScreenEntity
{
    /// <summary>The whole of <c>textures/backdrops/sky.png</c>, anchored at its top-left corner.</summary>
    private static readonly Sprite Dusk = new(CapsuleAssets.Textures.Backdrops.SkyTexture, new TextureRegion(0, 0, 320, 180));

    public Sky()
        : base(Anchor.TopLeft, Vector2.Zero)
    {
        SortsWithWorld = true;
        ZIndex = -20;
        Add(new SpriteRenderer(Dusk) { Tiling = new Vector2(float.PositiveInfinity, 0f) });
    }
}
