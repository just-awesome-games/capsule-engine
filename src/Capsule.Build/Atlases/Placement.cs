namespace Capsule.Build.Atlases;

/// <summary>Where one cell landed: its page and the top-left texel of its cell.</summary>
internal readonly record struct Placement(string Key, int Page, int X, int Y);
