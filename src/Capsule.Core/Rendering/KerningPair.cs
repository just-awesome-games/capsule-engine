namespace Capsule.Rendering;

/// <summary>How a <see cref="BitmapFont"/> tightens or loosens one ordered pair of codepoints.</summary>
/// <param name="First">The codepoint drawn first.</param>
/// <param name="Second">The codepoint drawn immediately after it, on the same line.</param>
/// <param name="Amount">
/// Font pixels added to the pen between the two, usually negative. Applied on top of
/// <see cref="Glyph.XAdvance"/>.
/// </param>
public readonly record struct KerningPair(int First, int Second, int Amount);
