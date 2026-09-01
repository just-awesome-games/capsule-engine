using Capsule.Scenes.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>
/// How a tile type's collision is spelled in a scene document, and in the authoring formats that
/// derive one. Absent means the tile type collides as nothing.
/// </summary>
public static class TileCollisionNames
{
    /// <summary>The whole tile, as a box.</summary>
    public const string Box = "box";

    /// <summary>The tile's up-facing edge alone.</summary>
    public const string OneWay = "one-way";

    /// <summary>Every name the field accepts, in the order they are documented.</summary>
    public static IReadOnlyList<string> All { get; } = [Box, OneWay];

    /// <summary>The collision <paramref name="name"/> spells, or false when it spells none of them.</summary>
    public static bool TryParse(string? name, out TileCollision collision)
    {
        switch (name)
        {
            case Box:
                collision = TileCollision.Solid;
                return true;
            case OneWay:
                collision = TileCollision.OneWay;
                return true;
            default:
                collision = TileCollision.None;
                return false;
        }
    }

    /// <summary>How <paramref name="collision"/> is written, or null when it is written not at all.</summary>
    public static string? Format(TileCollision collision) => collision switch
    {
        TileCollision.Solid => Box,
        TileCollision.OneWay => OneWay,
        _ => null,
    };
}
