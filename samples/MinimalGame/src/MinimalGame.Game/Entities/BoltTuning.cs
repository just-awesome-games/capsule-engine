namespace MinimalGame.Game.Entities;

/// <summary>
/// Every designer-owned lever a <see cref="Bolt"/> flies on, fixed for its lifetime. One world unit
/// is one texel of the sprite sheet.
/// </summary>
/// <param name="Speed">How fast the bolt travels, in px/s.</param>
/// <param name="LifetimeTicks">Fixed steps the bolt lives unless it strikes terrain first, which with <paramref name="Speed"/> sets its range.</param>
/// <param name="Size">The bolt's extent in world units, drawn as a soft tinted glow.</param>
/// <param name="Tint">The colour the bolt is drawn in.</param>
public readonly record struct BoltTuning(
    float Speed,
    int LifetimeTicks,
    Vector2 Size,
    ColorRgba Tint)
{
    /// <summary>The bolt the sample ships with: a short yellow dash that flies 120 px in half a second.</summary>
    public static readonly BoltTuning Default = new(
        Speed: 240f,
        LifetimeTicks: 30,
        Size: new Vector2(8f, 4f),
        Tint: ColorRgba.FromHex("#ffe040"));
}
