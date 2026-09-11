using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One run of text as the simulation wants it drawn.
/// <see cref="FrameView.Add(in TextIntent)"/> lays it out and adds one
/// <see cref="SpriteIntent"/> per glyph to the same ordered list every sprite goes on, so text
/// interpolates, culls and counts exactly as sprites do.
/// </summary>
/// <param name="Font">The font the run is laid out and drawn with; null draws nothing.</param>
/// <param name="Text">
/// The text drawn; null or empty draws nothing. <c>\n</c> starts a new line one
/// <see cref="BitmapFont.LineHeight"/> down, <c>\r</c> is ignored, and a codepoint the font carries
/// no glyph for draws nothing and advances nothing.
/// </param>
/// <param name="PreviousPosition">
/// Where the run's origin sat at the end of the previous step, in world units.
/// </param>
/// <param name="Position">
/// Where the run's origin sits now, in world units. The origin is the top-left corner of the first
/// line — the pen at the start of it, on that line's top edge, not on its baseline.
/// </param>
/// <param name="Scale">
/// Multiplies font pixels into world units per axis. <see cref="Vector2.One"/> draws one font pixel
/// per world unit; a non-positive component draws nothing.
/// </param>
/// <param name="Color">
/// Multiplied into every texel of every glyph; <see cref="ColorRgba.White"/> draws the pages as
/// they are.
/// </param>
public readonly record struct TextIntent(
    BitmapFont? Font,
    string? Text,
    Vector2 PreviousPosition,
    Vector2 Position,
    Vector2 Scale,
    ColorRgba Color);
