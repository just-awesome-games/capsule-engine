namespace Capsule.Build.Sheets;

/// <summary>One frame of a clip, held for <paramref name="Ticks"/> fixed steps and raising <paramref name="Events"/> as it starts.</summary>
internal readonly record struct SheetClipFrame(string Frame, int Ticks, string[] Events);
