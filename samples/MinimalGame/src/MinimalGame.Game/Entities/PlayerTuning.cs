using System.Numerics;

namespace MinimalGame.Game.Entities;

/// <summary>
/// Every designer-owned lever the player runs on, fixed for its lifetime. Speeds are world units
/// per second and accelerations per second squared; one world unit is one texel of the sprite sheet.
/// </summary>
/// <param name="WalkSpeed">Ground and air horizontal speed, px/s. Higher crosses the room sooner;
/// lower makes the walk deliberate.</param>
/// <param name="Gravity">Downward acceleration, px/s², in a Y-down world. Higher gives a snappier,
/// shorter arc; lower floats.</param>
/// <param name="JumpSpeed">Upward speed at take-off, px/s; an apex of about 40px against the default
/// <paramref name="Gravity"/>, clearing a two-tile ledge. Higher raises the ceiling.</param>
/// <param name="JumpStretch">The scale the sprite snaps to on take-off: tall and thin, the classic
/// platformer stretch. Tune the pair together — the product is what reads as volume, and nothing
/// enforces it.</param>
/// <param name="LandSquash">The scale the sprite snaps to on landing: wide and flat, the stretch
/// inverted.</param>
/// <param name="ScaleRecovery">Scale units per second each axis walks back towards 1 after an
/// impulse. At 1.6 the 0.4 of either impulse is spent in a quarter second; raise it for a snappier
/// recovery, lower it to let the deformation linger.</param>
/// <param name="MaxHealth">Hazard contacts the player survives; the health it starts a room
/// with.</param>
/// <param name="HurtboxInset">How far the hurtbox is drawn in from each of the body's edges, in
/// world units. A hurtbox smaller than the drawn frame is the grace 2D games give the player: a near
/// miss reads as a miss. Widen it towards zero to make hits generous, inset it further to make them
/// forgiving.</param>
public readonly record struct PlayerTuning(
    float WalkSpeed,
    float Gravity,
    float JumpSpeed,
    Vector2 JumpStretch,
    Vector2 LandSquash,
    float ScaleRecovery,
    int MaxHealth,
    int HurtboxInset)
{
    /// <summary>The feel the sample ships with.</summary>
    public static readonly PlayerTuning Default = new(
        WalkSpeed: 80f,
        Gravity: 600f,
        JumpSpeed: 220f,
        JumpStretch: new Vector2(0.6f, 1.4f),
        LandSquash: new Vector2(1.4f, 0.6f),
        ScaleRecovery: 1.6f,
        MaxHealth: 4,
        HurtboxInset: 1);
}
