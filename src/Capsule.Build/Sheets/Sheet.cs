namespace Capsule.Build.Sheets;

/// <summary>One validated sheet: the texture it cuts from, its sockets, boxes, events, frames and clips.</summary>
/// <param name="TextureKey">The texture's key.</param>
/// <param name="TextureExtension">The extension the sheet spelled the texture with.</param>
internal readonly record struct Sheet(
    string TextureKey,
    string TextureExtension,
    string[] Sockets,
    string[] Boxes,
    string[] Events,
    SheetFrame[] Frames,
    SheetClip[] Clips);

/// <summary>One region of the sheet's texture, with the pivot, sockets and boxes it carries.</summary>
internal readonly record struct SheetFrame(
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    float PivotX,
    float PivotY,
    SheetSocket[] Sockets,
    SheetBox[] Boxes);

/// <summary>One socket a frame sets, in the pivot's texel space.</summary>
internal readonly record struct SheetSocket(string Name, float X, float Y);

/// <summary>One box a frame sets, in the pivot's texel space.</summary>
internal readonly record struct SheetBox(string Name, float X, float Y, float Width, float Height);

/// <summary>One animation a sheet plays over its own frames.</summary>
internal readonly record struct SheetClip(string Name, bool Loop, SheetClipFrame[] Frames);

/// <summary>One frame of a clip, held for <paramref name="Ticks"/> fixed steps and raising <paramref name="Events"/> as it starts.</summary>
internal readonly record struct SheetClipFrame(string Frame, int Ticks, string[] Events);
