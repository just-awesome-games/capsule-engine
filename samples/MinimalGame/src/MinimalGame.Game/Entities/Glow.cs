namespace MinimalGame.Game.Entities;

/// <summary>The soft light the sample draws additively: the bolt's body, the lamp's head and the particles that fade to clear.</summary>
internal static class Glow
{
    /// <summary>The edge of <c>textures/glow.png</c> in texels.</summary>
    public const int Texels = 8;

    // A greyscale falloff pivoted at its centre, where an entity's scale sizes it about the position.
    // Its config makes it an r8 mask the renderer's colour tints, sampled linearly to stay soft in this
    // point-sampled game.
    private static readonly Sprite Falloff = new(CapsuleAssets.Textures.GlowTexture, new TextureRegion(0, 0, Texels, Texels), new Vector2(Texels / 2f));

    /// <summary>A renderer drawing the glow additively in <paramref name="color"/>.</summary>
    public static SpriteRenderer Renderer(ColorRgba color) => new(Falloff) { Color = color, Blend = BlendMode.Additive };

    /// <summary>A gradient from <paramref name="color"/> to the same colour at zero alpha.</summary>
    public static Gradient FadeToClear(ColorRgba color) => Gradient.Linear(color, color with { A = 0 });
}
