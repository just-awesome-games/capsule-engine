using System.Numerics;
using Capsule.Rendering;

namespace MinimalGame.Game.Entities;

/// <summary>
/// Every designer-owned lever a <see cref="Bolt"/> flies on, fixed for its lifetime. One world unit
/// is one texel of the sprite sheet.
/// </summary>
/// <param name="Speed">How fast the bolt travels, px/s. Higher reaches the far wall sooner and reads
/// as a lighter shot; lower makes each bolt something to watch.</param>
/// <param name="LifetimeTicks">Fixed steps the bolt lives before it is removed, whatever it met;
/// with <paramref name="Speed"/> this is its range. Raise it to cross the whole room, lower it for a
/// short-range spit.</param>
/// <param name="Size">The bolt's extent in world units, drawn as a flat tinted rect. Wider reads as
/// a beam, taller as a shell.</param>
/// <param name="Tint">The colour the bolt is drawn in.</param>
public readonly record struct BoltTuning(
    float Speed,
    int LifetimeTicks,
    Vector2 Size,
    ColorRgba Tint)
{
    /// <summary>The bolt the sample ships with: a short yellow dash that crosses the room in a second.</summary>
    public static readonly BoltTuning Default = new(
        Speed: 240f,
        LifetimeTicks: 30,
        Size: new Vector2(4f, 2f),
        Tint: new ColorRgba(255, 224, 64));
}
