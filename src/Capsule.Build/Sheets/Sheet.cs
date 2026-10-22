namespace Capsule.Build.Sheets;

/// <summary>One validated sheet: the texture it cuts from, its sockets, boxes, events, frames and clips.</summary>
/// <param name="TextureKey">The texture's key.</param>
/// <param name="TextureExtension">The extension the sheet spelled the texture with.</param>
/// <param name="VariantOf">The key of the sheet this one draws another way, or null for a sheet of its own.</param>
internal readonly record struct Sheet(
    string TextureKey,
    string TextureExtension,
    string[] Sockets,
    string[] Boxes,
    string[] Events,
    SheetFrame[] Frames,
    SheetClip[] Clips,
    string? VariantOf);

/// <summary>One region of the sheet's texture, with the pivot, sockets and boxes it carries.</summary>
/// <param name="IsEmpty">Whether a variant's frame names no region and draws nothing. Its region and pivot are then zero.</param>
internal readonly record struct SheetFrame(
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    float PivotX,
    float PivotY,
    SheetSocket[] Sockets,
    SheetBox[] Boxes,
    bool IsEmpty = false)
{
    /// <summary>Whether this frame and <paramref name="other"/> draw the same region from the same pivot.</summary>
    internal bool DrawsAs(in SheetFrame other) =>
        IsEmpty == other.IsEmpty && X == other.X && Y == other.Y && Width == other.Width && Height == other.Height
        && PivotX.Equals(other.PivotX) && PivotY.Equals(other.PivotY);
}

/// <summary>One socket a frame sets, in the pivot's texel space.</summary>
internal readonly record struct SheetSocket(string Name, float X, float Y);

/// <summary>One box a frame sets, in the pivot's texel space.</summary>
internal readonly record struct SheetBox(string Name, float X, float Y, float Width, float Height);

/// <summary>One animation a sheet plays over its own frames.</summary>
internal readonly record struct SheetClip(string Name, bool Loop, SheetClipFrame[] Frames);

/// <summary>One frame of a clip, held for <paramref name="Ticks"/> fixed steps and raising <paramref name="Events"/> as it starts.</summary>
internal readonly record struct SheetClipFrame(string Frame, int Ticks, string[] Events);

/// <summary>A variant sheet with the base whose frames it draws another way, both validated.</summary>
/// <param name="BaseKey">The base sheet's key.</param>
internal readonly record struct SheetVariant(Sheet Variant, string BaseKey, Sheet Base);
