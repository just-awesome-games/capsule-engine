namespace Capsule.Text;

/// <summary>A filled cell in a laid-out string, in glyph-grid coordinates (origin top-left).</summary>
public readonly record struct GridCell(int X, int Y);
