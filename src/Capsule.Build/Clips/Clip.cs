namespace Capsule.Build.Clips;

/// <summary>One validated value clip: whether it loops, how it moves between keys, and its keys.</summary>
internal readonly record struct Clip(bool Loop, ClipBlend Blend, ClipKey[] Keys);

/// <summary>One key of a clip: a finite value held, or moved from, for <paramref name="Ticks"/> fixed steps.</summary>
internal readonly record struct ClipKey(float Value, int Ticks);
