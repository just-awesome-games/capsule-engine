using Capsule.Collision;

namespace Capsule.Scenes.Documents;

/// <summary>
/// How a tile type's collidable sides are spelled in a scene document. Named by grid direction in a
/// Y-down world, so <see cref="Top"/> is the side a falling body lands on; an absent list is all.
/// </summary>
public static class TileFaceNames
{
    /// <summary>The tile's -X side.</summary>
    public const string Left = "left";

    /// <summary>The tile's +X side.</summary>
    public const string Right = "right";

    /// <summary>The tile's -Y side.</summary>
    public const string Top = "top";

    /// <summary>The tile's +Y side.</summary>
    public const string Bottom = "bottom";

    /// <summary>Every name the field accepts, in the order they are documented.</summary>
    public static IReadOnlyList<string> All { get; } = [Left, Right, Top, Bottom];

    /// <summary>The face <paramref name="name"/> spells, or false when it spells none of them.</summary>
    public static bool TryParse(string? name, out CellFaces2D face)
    {
        switch (name)
        {
            case Left:
                face = CellFaces2D.Left;
                return true;
            case Right:
                face = CellFaces2D.Right;
                return true;
            case Top:
                face = CellFaces2D.Top;
                return true;
            case Bottom:
                face = CellFaces2D.Bottom;
                return true;
            default:
                face = CellFaces2D.None;
                return false;
        }
    }

    /// <summary>
    /// How <paramref name="faces"/> is written, in <see cref="All"/> order, or null for
    /// <see cref="CellFaces2D.All"/>, which is the absent list.
    /// </summary>
    public static string[]? Format(CellFaces2D faces)
    {
        if (faces == CellFaces2D.All)
        {
            return null;
        }

        List<string> names = [];
        foreach (string name in All)
        {
            if (TryParse(name, out CellFaces2D face) && (faces & face) != 0)
            {
                names.Add(name);
            }
        }

        return [.. names];
    }
}
