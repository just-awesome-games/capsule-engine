using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;

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
/// platformer stretch. Tune the pair together: the product is what reads as volume, and nothing
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
/// <param name="LandRumbleLow">The low-frequency motor's amplitude on landing, in [0, 1]. The heavy
/// motor is the thump. Raise it for a weightier body, or drop it to zero for a light one.</param>
/// <param name="LandRumbleHigh">The high-frequency motor's amplitude on landing, in [0, 1]. The
/// light motor is the click at the top of the thump. Keep it below the low.</param>
/// <param name="LandRumbleSeconds">How long the landing pulse decays over. Shorter reads as a tap,
/// longer as a heavy body settling.</param>
/// <param name="HurtRumble">The pulse a hazard contact plays: both motors and both impulse triggers,
/// decaying. Raise the amplitudes or the seconds to make a hit land harder.</param>
/// <param name="InvulnerableTicks">Steps of grace after a hit, during which hazards cost nothing. At
/// 60 steps a second the default is one second. Raise it to forgive a player bounced between hazards,
/// lower it to punish lingering.</param>
/// <param name="BlinkTicks">Steps the sprite spends shown, then hidden, in each half of the grace's
/// blink. Lower flickers faster and reads as more urgent; higher reads as a slow pulse.</param>
/// <param name="HurtTint">The colour multiplied over the sprite through the grace. Push it towards
/// pure red for a harsher hit, towards white to keep the blink alone.</param>
public readonly record struct PlayerTuning(
    float WalkSpeed,
    float Gravity,
    float JumpSpeed,
    Vector2 JumpStretch,
    Vector2 LandSquash,
    float ScaleRecovery,
    int MaxHealth,
    int HurtboxInset,
    float LandRumbleLow,
    float LandRumbleHigh,
    float LandRumbleSeconds,
    RumblePulse HurtRumble,
    int InvulnerableTicks,
    int BlinkTicks,
    ColorRgba HurtTint)
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
        HurtboxInset: 1,
        LandRumbleLow: 0.5f,
        LandRumbleHigh: 0.15f,
        LandRumbleSeconds: 0.12f,
        HurtRumble: new RumblePulse(0.8f, 0.5f, 0.3f) { LeftTrigger = 0.6f, RightTrigger = 0.6f },
        InvulnerableTicks: 60,
        BlinkTicks: 4,
        HurtTint: new ColorRgba(255, 96, 96));
}
