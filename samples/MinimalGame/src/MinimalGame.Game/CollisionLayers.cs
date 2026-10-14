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
    /// The layer of the player's probe, which blocks nothing. A trigger area, such as a camera zone, a hint
    /// or a doorway, detects it and reacts itself.
    /// </summary>
    public const string Probe = "probe";

    public const string Hazard = "hazard";

    public static readonly CollisionMask Blocking = new(Solid, Platform);
}
