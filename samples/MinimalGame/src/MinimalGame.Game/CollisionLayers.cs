using Capsule.Physics;

namespace MinimalGame.Game;

public static class CollisionLayers
{
    /// <summary>The layer the player's body is on.</summary>
    public const string Player = "player";

    /// <summary>The layer the floor and walls are on, and what the player collides with.</summary>
    public const string Solid = "solid";

    public const string Platform = "platform";

    /// <summary>
    /// The layer of passive areas the player's probe finds, such as a camera zone's. Several drive one shared
    /// thing, so they react to nothing: its owner reads the probe's contacts.
    /// </summary>
    public const string Trigger = "trigger";

    /// <summary>
    /// The layer of the player's probe, which blocks nothing. An area whose effect is its own, such as a hint
    /// or a doorway, detects it and reacts itself.
    /// </summary>
    public const string Probe = "probe";

    public const string Hazard = "hazard";

    public static readonly CollisionMask Blocking = new(Solid, Platform);
}
