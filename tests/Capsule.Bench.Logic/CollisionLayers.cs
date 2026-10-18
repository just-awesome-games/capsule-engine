namespace Capsule.Bench.Logic;

public static class CollisionLayers
{
    public const string Solid = "solid";

    /// <summary>A one-way ledge, collidable from above.</summary>
    public const string Platform = "platform";

    /// <summary>Walkers and posts; walkers never collide with each other.</summary>
    public const string Actor = "actor";

    /// <summary>A solid object that shoves the bodies it moves into.</summary>
    public const string Crate = "crate";

    /// <summary>A box no body is on, which blocks nothing.</summary>
    public const string Sensor = "sensor";
}
