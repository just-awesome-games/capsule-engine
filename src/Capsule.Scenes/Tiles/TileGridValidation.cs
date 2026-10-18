using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Physics;

namespace Capsule.Tiles;

// The checks a TileGrid runs on what it is built from. Every refusal is an ArgumentException naming the
// constructor parameter at fault.
internal static class TileGridValidation
{
    // Checks everything but the transforms, and returns each palette entry's collision polygon, or null
    // for a whole tile.
    internal static Shape2D?[] Validate(
        int tileSize,
        int width,
        int height,
        TileType[] tileTypes,
        int[] tiles,
        TextureHandle? texture,
        int columns)
    {
        if (tileSize <= 0)
        {
            throw Malformed($"tileSize must be positive, not {tileSize}.", "tileSize");
        }

        if (width <= 0)
        {
            throw Malformed($"width must be positive, not {width}.", "width");
        }

        if (height <= 0)
        {
            throw Malformed($"height must be positive, not {height}.", "height");
        }

        Shape2D?[] shapes = ValidatePalette(tileTypes, tileSize);
        ValidateTexture(tileTypes, texture, columns, tileSize);
        ValidateTiles(tiles, width, height, tileTypes.Length);

        return shapes;
    }

    internal static void ValidateTransforms(byte[] transforms, int width, int height, int tileCount)
    {
        if (transforms.Length != tileCount)
        {
            throw Malformed(
                $"transforms has {transforms.Length} entries but width {width} x height {height} requires {tileCount}. Give one per tile, or pass null for none.",
                "transforms");
        }

        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] >= TileTransforms.Count)
            {
                throw Malformed(
                    $"transforms[{i}] is {transforms[i]}. Use a TileTransform in 0..{TileTransforms.Count - 1}.",
                    "transforms");
            }
        }
    }

    // Each authored transform as the TileTransform it stands for. A byte cast alone would wrap an out-of-range value.
    internal static byte[] Turned(int[] authored)
    {
        byte[] facings = new byte[authored.Length];
        for (int i = 0; i < facings.Length; i++)
        {
            facings[i] = authored[i] is >= 0 and < TileTransforms.Count
                ? (byte)authored[i]
                : throw Malformed(
                    $"transforms[{i}] is {authored[i]}. Use 0 for a tile as authored, or add 1 to mirror it left to right, 2 to mirror it top to bottom and 4 to swap its axes, up to {TileTransforms.Count - 1}.",
                    "transforms");
        }

        return facings;
    }

    private static Shape2D?[] ValidatePalette(TileType[] tileTypes, int tileSize)
    {
        int missing = Array.IndexOf(tileTypes, null);
        if (missing >= 0)
        {
            throw Malformed($"tileTypes[{missing}] is null. Give every palette entry a tile type.", "tileTypes");
        }

        if (tileTypes.Length == 0 || !IsEmpty(tileTypes[0]))
        {
            string actual = tileTypes.Length == 0
                ? "an empty palette"
                : $"\"{tileTypes[0].Name}\" with cell {tileTypes[0].Cell?.ToString() ?? "none"} and layer {tileTypes[0].Layer ?? "none"}";
            throw Malformed(
                $"tileTypes[0] is {actual}. Make it a plain TileType named \"{TileGrid.EmptyTileName}\" with no cell, no frames, no layer and no properties.",
                "tileTypes");
        }

        Shape2D?[] shapes = new Shape2D?[tileTypes.Length];
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < tileTypes.Length; i++)
        {
            TileType tileType = tileTypes[i];

            if (string.IsNullOrWhiteSpace(tileType.Name))
            {
                throw Malformed($"tileTypes[{i}] has no name. Name every tile type.", "tileTypes");
            }

            if (!seen.Add(tileType.Name))
            {
                throw Malformed($"tileTypes[{i}] repeats \"{tileType.Name}\". Give every tile type a unique name.", "tileTypes");
            }

            if (tileType.Cell is { } cell && cell < 0)
            {
                throw Malformed($"{Palette(tileType, i)} draws cell {cell}. Count cells from 0.", "tileTypes");
            }

            if (tileType.Frames is { } frames)
            {
                ValidateFrames(tileType, frames, i);
            }

            if (tileType.Layer is { } layer && string.IsNullOrWhiteSpace(layer))
            {
                throw Malformed($"{Palette(tileType, i)} has a blank layer. Name the layer a colliding tile is on.", "tileTypes");
            }

            if (tileType.Layer is null && (tileType.Shape is not null || tileType.OneWay))
            {
                throw Malformed(
                    $"{Palette(tileType, i)} declares {(tileType.Shape is null ? "oneWay" : "a shape")} but no layer and collides as nothing. Add a layer or drop it.",
                    "tileTypes");
            }

            if (!float.IsFinite(tileType.SurfaceVelocity.X) || !float.IsFinite(tileType.SurfaceVelocity.Y))
            {
                throw Malformed($"{Palette(tileType, i)} has a surfaceVelocity that is not finite. Give it finite world units per second.", "tileTypes");
            }

            if (tileType.Layer is null && tileType.SurfaceVelocity != Vector2.Zero)
            {
                throw Malformed(
                    $"{Palette(tileType, i)} declares a surfaceVelocity but no layer and carries nothing. Add a layer or drop it.",
                    "tileTypes");
            }

            if (tileType.SolidSides && !tileType.OneWay)
            {
                throw Malformed(
                    $"{Palette(tileType, i)} declares solidSides but no oneWay. Add oneWay, or drop solidSides for a tile solid from every side.",
                    "tileTypes");
            }

            if (tileType.Shape is { } points)
            {
                shapes[i] = ShapeOf(tileType, points, i, tileSize);
            }
        }

        return shapes;
    }

    private static void ValidateFrames(TileType tileType, TileFrame[] frames, int index)
    {
        if (tileType.Cell is not null)
        {
            throw Malformed(
                $"{Palette(tileType, index)} sets both cell and frames. Keep cell for a still tile, or frames for an animated one.",
                "tileTypes");
        }

        if (frames.Length == 0)
        {
            throw Malformed($"{Palette(tileType, index)} has no frames. Give it at least one frame, or drop frames.", "tileTypes");
        }

        for (int frame = 0; frame < frames.Length; frame++)
        {
            (int cell, int ticks) = frames[frame];
            if (cell < 0)
            {
                throw Malformed($"{Palette(tileType, index)}.frames[{frame}] draws cell {cell}. Count cells from 0.", "tileTypes");
            }

            if (ticks < 1)
            {
                throw Malformed(
                    $"{Palette(tileType, index)}.frames[{frame}] is held for {ticks} ticks. Hold every frame for at least 1 tick.",
                    "tileTypes");
            }
        }
    }

    // A tile's shape is a convex polygon inside its own tile.
    private static Shape2D ShapeOf(TileType tileType, Vector2[] points, int index, int tileSize)
    {
        Shape2D shape;
        try
        {
            shape = Shape2D.Polygon(points);
        }
        catch (ArgumentException ex)
        {
            throw Malformed($"{Palette(tileType, index)}.shape is not a convex polygon of 3 to {Shape2D.MaxPoints} points. {ex.Message}", "tileTypes");
        }

        Aabb2D bounds = shape.Bounds;
        if (bounds.Min.X < 0f || bounds.Min.Y < 0f || bounds.Max.X > tileSize || bounds.Max.Y > tileSize)
        {
            throw Malformed(
                $"{Palette(tileType, index)} has a shape point outside its tile. Keep every point within [0, {tileSize}] on both axes, measured from the tile's top-left corner.",
                "tileTypes");
        }

        return shape;
    }

    // Checks both directions. A cell without a texture or a texture without a cell is a half-written grid.
    private static void ValidateTexture(TileType[] tileTypes, TextureHandle? texture, int columns, int tileSize)
    {
        bool drawn = false;
        for (int i = 0; i < tileTypes.Length; i++)
        {
            TileType tileType = tileTypes[i];
            if (tileType.Cell is null && tileType.Frames is null)
            {
                continue;
            }

            drawn = true;

            if (texture is null)
            {
                string draws = tileType.Cell is { } cell ? $"cell {cell}" : "frames";
                throw Malformed(
                    $"{Palette(tileType, i)} draws {draws} but the grid names no texture. Give the grid a texture to cut from.",
                    "tileTypes");
            }
        }

        if (texture is not { } named)
        {
            if (columns != 0)
            {
                throw Malformed(
                    $"columns is {columns} on a grid that names no texture. Set columns to 0, or name a texture.",
                    "columns");
            }

            return;
        }

        if (!drawn)
        {
            throw Malformed(
                $"the grid names texture \"{named.Name}\" but no tile type draws a cell of it. Give a tile type a cell or frames, or drop the texture.",
                "texture");
        }

        if (columns < 1)
        {
            throw Malformed(
                $"columns must be at least 1 on a textured grid, not {columns}.",
                "columns");
        }

        ValidateCellRegions(tileTypes, columns, tileSize);
    }

    // Runs only after columns is known positive.
    private static void ValidateCellRegions(TileType[] tileTypes, int columns, int tileSize)
    {
        for (int i = 0; i < tileTypes.Length; i++)
        {
            if (tileTypes[i].Cell is { } cell)
            {
                ValidateCellRegion(tileTypes, cell, i, string.Empty, columns, tileSize);
            }

            if (tileTypes[i].Frames is { } frames)
            {
                for (int frame = 0; frame < frames.Length; frame++)
                {
                    ValidateCellRegion(tileTypes, frames[frame].Cell, i, $".frames[{frame}]", columns, tileSize);
                }
            }
        }
    }

    // Computes in long. A cell far enough down the atlas overflows int, and the wrapped coordinate would cut
    // the wrong region.
    private static void ValidateCellRegion(TileType[] tileTypes, int cell, int index, string frame, int columns, int tileSize)
    {
        long x = (long)(cell % columns) * tileSize;
        long y = (long)(cell / columns) * tileSize;

        if (x + tileSize > int.MaxValue || y + tileSize > int.MaxValue)
        {
            throw Malformed(
                $"tileTypes[{index}]{frame} (\"{tileTypes[index].Name}\") draws cell {cell}, whose source region starts at ({x}, {y}) texels across {columns} columns of {tileSize}px, beyond the reach of a texture coordinate. Lower the cell number or the tile size.",
                "tileTypes");
        }
    }

    private static void ValidateTiles(int[] tiles, int width, int height, int paletteLength)
    {
        // Computes in long. An int product wraps, and 65536 x 65536 wrapping to 0 would let an empty tiles
        // array pass here and fail later when a cell is read.
        long expected = (long)width * height;
        if (tiles.Length != expected)
        {
            throw Malformed(
                $"tiles has {tiles.Length} entries but width {width} x height {height} requires {expected}.",
                "tiles");
        }

        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i] < 0 || tiles[i] >= paletteLength)
            {
                throw Malformed(
                    $"tiles[{i}] is {tiles[i]}. Use a tileTypes index in 0..{paletteLength - 1}.",
                    "tiles");
            }
        }
    }

    // The empty entry draws, collides and authors nothing. A subclass would fill every unpainted cell.
    private static bool IsEmpty(TileType tileType) =>
        tileType is { Name: TileGrid.EmptyTileName, Cell: null, Frames: null, Layer: null, Shape: null, OneWay: false, SolidSides: false, SurfaceVelocity: { X: 0f, Y: 0f } }
        && tileType.GetType() == typeof(TileType);

    // How a message names a palette entry: its index, and the name an author finds it by.
    private static string Palette(TileType tileType, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"tileTypes[{index}] (\"{tileType.Name}\")");

    private static ArgumentException Malformed(string message, string parameterName) => new(message, parameterName);
}
