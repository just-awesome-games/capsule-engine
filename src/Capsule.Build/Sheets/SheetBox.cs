namespace Capsule.Build.Sheets;

/// <summary>One box a frame sets, in the pivot's texel space.</summary>
internal readonly record struct SheetBox(string Name, float X, float Y, float Width, float Height);
