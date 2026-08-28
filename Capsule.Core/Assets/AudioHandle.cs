namespace Capsule.Assets;

/// <summary>Pure data naming <c>assets/audio/{Name}{Extension}</c> beside the executable.</summary>
public readonly record struct AudioHandle(string Name, string Extension);
