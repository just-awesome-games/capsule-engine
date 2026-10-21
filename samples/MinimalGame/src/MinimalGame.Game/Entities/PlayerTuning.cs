namespace MinimalGame.Game.Entities;

/// <summary>
/// Every designer-owned lever the player runs on, fixed for its lifetime. One world unit is one texel
/// of the sprite sheet.
/// </summary>
/// <param name="WalkSpeed">Ground and air horizontal speed, in px/s.</param>
/// <param name="SlopeSpeed">How much a slope slows the climb and speeds the descent, where 0 walks a slope at <paramref name="WalkSpeed"/>.</param>
/// <param name="StepHeight">The tallest lip the player walks up onto and the deepest drop it walks down without leaving the ground, in px.</param>
/// <param name="Gravity">Downward acceleration in a Y-down world, in px/s².</param>
/// <param name="JumpSpeed">Upward speed at take-off, in px/s.</param>
/// <param name="CoyoteTicks">Steps after walking off a ledge in which a jump still takes off, where 0 needs the floor underfoot.</param>
/// <param name="JumpBufferTicks">Steps a jump pressed in the air is kept for a landing, where 0 drops the press.</param>
/// <param name="JumpStretch">The scale the sprite snaps to on take-off.</param>
/// <param name="LandSquash">The scale the sprite snaps to on landing.</param>
/// <param name="ScaleRecovery">Scale units per second each axis walks back towards 1 after an impulse.</param>
/// <param name="MaxHealth">Hazard contacts the player survives, and the health it starts a room with.</param>
/// <param name="HurtboxInset">How far the hurtbox is drawn in from each of the body's edges, in world units.</param>
/// <param name="LandRumbleLow">The low-frequency motor's amplitude on landing, in [0, 1].</param>
/// <param name="LandRumbleHigh">The high-frequency motor's amplitude on landing, in [0, 1].</param>
/// <param name="LandRumbleSeconds">How long the landing pulse decays over.</param>
/// <param name="HurtRumble">The pulse a hazard contact plays.</param>
/// <param name="InvulnerableTicks">Steps of grace after a hit, during which hazards cost nothing.</param>
/// <param name="BlinkTicks">Steps the sprite spends shown, then hidden, in each half of the grace's blink.</param>
/// <param name="HurtTint">The colour multiplied over the sprite through the grace.</param>
/// <param name="HurtFlashTicks">Steps the white flash on a hit takes to fade.</param>
/// <param name="HurtFreezeTicks">Steps the room holds still when a hit lands.</param>
/// <param name="HurtShake">How hard the camera shakes when a hit lands, from 0 to 1.</param>
/// <param name="HardLandingSpeed">The fall speed at or above which a landing thuds the camera, in px/s.</param>
/// <param name="HardLandingShake">The thud a hard landing gives the camera.</param>
public sealed record class PlayerTuning(
    float WalkSpeed,
    float SlopeSpeed,
    float StepHeight,
    float Gravity,
    float JumpSpeed,
    int CoyoteTicks,
    int JumpBufferTicks,
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
    ColorRgba HurtTint,
    int HurtFlashTicks,
    int HurtFreezeTicks,
    float HurtShake,
    float HardLandingSpeed,
    CameraShake HardLandingShake)
{
    /// <summary>The feel the sample ships with.</summary>
    public static readonly PlayerTuning Default = new(
        WalkSpeed: 80f,
        SlopeSpeed: 0.3f,
        StepHeight: 5f,
        Gravity: 600f,
        JumpSpeed: 220f,
        CoyoteTicks: 6,
        JumpBufferTicks: 6,
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
        HurtTint: new ColorRgba(255, 96, 96),
        HurtFlashTicks: 10,
        HurtFreezeTicks: 6,
        HurtShake: 0.6f,
        HardLandingSpeed: 300f,
        HardLandingShake: new CameraShake(new Vector2(0f, 2f), 0.15f) { Wave = ShakeWave.Sine, Frequency = 30f });
}
