namespace Capsule.Assets;

/// <summary>Pure data naming <c>assets/fonts/{Name}{Extension}</c> beside the executable.</summary>
public readonly record struct FontHandle(string Name, string Extension);
