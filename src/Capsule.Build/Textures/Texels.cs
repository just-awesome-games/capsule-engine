namespace Capsule.Build.Textures;

/// <summary>Texels in rows, <paramref name="Channels"/> bytes each and no padding.</summary>
internal readonly record struct Texels(byte[] Data, int Width, int Height, int Channels);
