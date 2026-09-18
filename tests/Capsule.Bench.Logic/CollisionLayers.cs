namespace Capsule.Bench.Logic;

public static class CollisionLayers
{
    public const string Solid = "solid";

    /// <summary>A one-way ledge, collidable from above.</summary>
    public const string Platform = "platform";

    /// <summary>Walkers and posts; walkers never collide with each other.</summary>
    public const string Actor = "actor";
}
