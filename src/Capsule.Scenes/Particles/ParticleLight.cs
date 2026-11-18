using Capsule.Rendering;

namespace Capsule.Particles;

/// <summary>
/// The light each live particle of a <see cref="ParticleEmitter"/> adds into the frame's light, placed, turned and
/// sized as its particle and coloured by the emitter's <see cref="ParticleEmitter.Color"/> at the particle's age.
/// </summary>
/// <remarks>
/// The gradient's alpha scales the light, and a particle fading to clear fades its light with it. A light-only
/// effect is an emitter built from its capacity alone, which draws no sprite.
/// </remarks>
/// <example>
/// Motes that light what they drift past:
/// <code>
/// Light = new ParticleLight(radius: 12f, intensity: 0.5f),
/// </code>
/// </example>
public readonly record struct ParticleLight
{
    /// <summary>
    /// World units from a particle to its light's falloff edge along the sprite's wider axis, at a particle scale of one.
    /// </summary>
    /// <remarks>The other axis keeps the sprite's aspect, and the particle's scale multiplies both.</remarks>
    public float Radius { get; }

    /// <summary>How many times each light's colour is added. Zero adds nothing.</summary>
    public float Intensity { get; }

    /// <summary>The frame each light is drawn as. <see cref="Rendering.Sprite.Light"/> by default, the engine's radial falloff.</summary>
    /// <remarks>
    /// A light adds the frame's colour times its alpha, as a sprite drawn additively would. Author a falloff in the
    /// alpha of a white image, or in the grey of an opaque one, but not in both, which squares it.
    /// </remarks>
    public Sprite Sprite { get; init; } = Sprite.Light;

    /// <summary>Creates a light of <paramref name="radius"/> added <paramref name="intensity"/> times.</summary>
    /// <param name="radius">World units from a particle to its light's falloff edge, at a particle scale of one.</param>
    /// <param name="intensity">How many times each light's colour is added, 1 by default.</param>
    public ParticleLight(float radius, float intensity = 1f)
    {
        Radius = Guard.NonNegative(radius, nameof(radius));
        Intensity = Guard.NonNegative(intensity, nameof(intensity));
    }
}
