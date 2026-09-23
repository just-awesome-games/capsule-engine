namespace Capsule.Rendering;

/// <summary>How a sprite's colour combines with what is already drawn.</summary>
public enum BlendMode : byte
{
    /// <summary>The sprite covers what is behind it by its alpha.</summary>
    Alpha,

    /// <summary>The sprite adds its colour scaled by its alpha and covers nothing behind it, for glow, sparks and fire, and is a light in a lit scene.</summary>
    Additive,
}
