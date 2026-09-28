namespace Capsule.Build.Sheets;

/// <summary>One animation a sheet plays over its own frames.</summary>
internal readonly record struct SheetClip(string Name, bool Loop, SheetClipFrame[] Frames);
