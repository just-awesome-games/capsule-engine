namespace Capsule.Build.Sheets;

/// <summary>One validated sheet: the texture it cuts from, its sockets, frames and clips.</summary>
/// <param name="TextureKey">The texture's key.</param>
/// <param name="TextureExtension">The extension the sheet spelled the texture with.</param>
internal readonly record struct Sheet(
    string TextureKey,
    string TextureExtension,
    string[] Sockets,
    SheetFrame[] Frames,
    SheetClip[] Clips);
