namespace MinimalGame.Game;

public static class CollisionLayers
{
    /// <summary>The layer the player runs on, and the one the camera follows.</summary>
    public const string Player = "player";

    /// <summary>The layer the floor and walls are on, and what the player collides with.</summary>
    public const string Solid = "solid";

    public const string Platform = "platform";

    public const string Hazard = "hazard";

    public const string Enemy = "enemy";

    public static ReadOnlySpan<string> Blocking => BlockingLayers;

    public static ReadOnlySpan<string> Damaging => DamagingLayers;

    private static readonly string[] BlockingLayers = [Solid, Platform];

    private static readonly string[] DamagingLayers = [Hazard, Enemy];
}
