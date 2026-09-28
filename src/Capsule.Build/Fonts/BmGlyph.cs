namespace Capsule.Build.Fonts;

// One glyph as the font declares it. Page holds the font's page id until the parser resolves it to
// an index into PageFiles.
internal readonly record struct BmGlyph(
    int Codepoint,
    int Page,
    int X,
    int Y,
    int Width,
    int Height,
    int XOffset,
    int YOffset,
    int XAdvance);
