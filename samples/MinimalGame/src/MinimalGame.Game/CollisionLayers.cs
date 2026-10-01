using Capsule.Physics;

namespace MinimalGame.Game;

public static class CollisionLayers
{
    /// <summary>The layer the player's body is on.</summary>
    public const string Player = "player";

    /// <summary>The layer the floor and walls are on, and what the player collides with.</summary>
    public const string Solid = "solid";

    public const string Platform = "platform";

    public const string Hazard = "hazard";

    public static readonly CollisionMask Blocking = new(Solid, Platform);
}
