using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Build.Sprites;

/// <summary>One frame of a sheet: its texel region and the pivot inside it.</summary>
/// <param name="Name">The frame's name, unique within the sheet and safe as a C# identifier.</param>
/// <param name="Region">The frame's rectangle, in texels of the sheet's texture.</param>
/// <param name="Pivot">The anchor, in texels of <paramref name="Region"/> from its top-left corner.</param>
public sealed record SpriteSheetFrame(string Name, TextureRegion Region, Vector2 Pivot);

/// <summary>One entry of a clip: a frame of the same sheet, held for a whole number of fixed steps.</summary>
/// <param name="Frame">The name of a frame of this sheet.</param>
/// <param name="Ticks">How many fixed steps the frame is held for; at least one.</param>
public sealed record SpriteSheetClipFrame(string Frame, int Ticks);

/// <summary>One named animation of a sheet.</summary>
/// <param name="Name">The clip's name, unique within the sheet and safe as a C# identifier.</param>
/// <param name="Loop">Whether the last entry wraps back to the first.</param>
/// <param name="Frames">The entries in play order; at least one.</param>
public sealed record SpriteSheetClip(string Name, bool Loop, IReadOnlyList<SpriteSheetClipFrame> Frames);

/// <summary>
/// What derived a document, kept verbatim so a sheet an authoring module wrote names the file a
/// person edited.
/// </summary>
/// <param name="Tool">The module that derived the document.</param>
/// <param name="Path">The source it was derived from, relative to the importing project.</param>
/// <param name="Hash">A hash of the source closure.</param>
public sealed record SpriteSheetSource(string Tool, string Path, string Hash);

/// <summary>
/// One sprite sheet: the texture it cuts from, its named frames, and the clips over them. Build-time
/// data only — the build turns it into game code, and no sheet ships beside the executable.
/// </summary>
/// <param name="Texture">The texture every frame is cut from.</param>
/// <param name="Frames">The sheet's frames in authored order; at least one.</param>
/// <param name="Clips">The sheet's clips in authored order; at least one.</param>
/// <param name="Source">The authoring module's provenance, absent on a hand-authored sheet.</param>
public sealed record SpriteSheetDocument(
    TextureHandle Texture,
    IReadOnlyList<SpriteSheetFrame> Frames,
    IReadOnlyList<SpriteSheetClip> Clips,
    SpriteSheetSource? Source = null);
