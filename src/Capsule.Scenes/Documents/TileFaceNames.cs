using Capsule.Physics;

namespace Capsule.Scenes.Documents;

/// <summary>
/// The names a scene document uses for a tile type's collidable sides. They are named by grid direction in a
/// Y-down world, so <see cref="Top"/> is the side a falling body lands on. An absent list means every side.
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

    /// <summary>Every name the field accepts, in documented order.</summary>
    public static IReadOnlyList<string> All { get; } = [Left, Right, Top, Bottom];

    /// <summary>Parses <paramref name="name"/> into a face, and returns false when it names no face.</summary>
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
    /// Formats <paramref name="faces"/> in <see cref="All"/> order, or returns null for
    /// <see cref="CellFaces2D.All"/>, which the document writes as an absent list.
    /// </summary>
    public static string[]? Format(CellFaces2D faces)
    {
        if (faces == CellFaces2D.All)
        {
            return null;
        }

        string[] names = new string[4];
        int written = 0;

        if ((faces & CellFaces2D.Left) != 0)
        {
            names[written++] = Left;
        }

        if ((faces & CellFaces2D.Right) != 0)
        {
            names[written++] = Right;
        }

        if ((faces & CellFaces2D.Top) != 0)
        {
            names[written++] = Top;
        }

        if ((faces & CellFaces2D.Bottom) != 0)
        {
            names[written++] = Bottom;
        }

        return names[..written];
    }
}
