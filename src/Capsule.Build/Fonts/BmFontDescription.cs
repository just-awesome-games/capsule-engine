namespace Capsule.Build.Fonts;

// What the build reads out of one '.fnt'. Pages are in page-id order, glyphs ascend by codepoint,
// and kernings ascend by pair.
internal sealed record BmFontDescription(int LineHeight, int Base, string[] PageFiles, BmGlyph[] Glyphs, BmKerning[] Kernings);
