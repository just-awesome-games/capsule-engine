using System.Numerics;

namespace Capsule.Scenes.Spawning;

/// <summary>Where and how an entity is placed, as its constructor receives it.</summary>
/// <remarks>
/// A document placement arrives as a spawn, and code builds one to place the same entity through the
/// same constructor. The <see cref="Entity(EntitySpawn)"/> constructor applies it.
/// <para>
/// Build one with <c>new EntitySpawn(position)</c>. <c>default(EntitySpawn)</c> skips the initializers
/// and has a scale of zero.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// scene.Add(new Spike(new EntitySpawn(position) { Rotation = MathF.PI / 2f }));
/// scene.Add(new Lift(new EntitySpawn(position)) { Rise = 96f });
/// </code>
/// </example>
/// <param name="Position">
/// The raw placed coordinate, which the entity's constructor translates to its own anchor.
/// </param>
public readonly record struct EntitySpawn(Vector2 Position)
{
    /// <summary>The placement's id in the document's id space, or 0 for a spawn built in code.</summary>
    public int Id { get; internal init; }

    /// <summary>The spawn type the entity claimed, or null for a spawn built in code.</summary>
    public string? Type { get; internal init; }

    /// <summary>
    /// The turn in radians, clockwise on screen, defaulting to 0. It becomes <see cref="Entity.Rotation"/>.
    /// </summary>
    public float Rotation { get; init; }

    /// <summary>
    /// The raw scale factors, defaulting to one. The entity's constructor decides what they mean: its own
    /// <see cref="Entity.Scale"/>, a collider shape run through <see cref="Capsule.Physics.Shape2D.Scaled"/>,
    /// or nothing.
    /// </summary>
    public Vector2 Scale { get; init; } = Vector2.One;

    /// <summary>The draw band, or null to keep the entity's own.</summary>
    public int? ZIndex { get; init; }

    /// <summary>The scroll factor, or null to keep the entity's own.</summary>
    public Vector2? ScrollFactor { get; init; }

    // A placement's authored member values and its class's generated applier. A spawn built in code
    // carries neither, and the members stay out of the record's printed form.
    internal AuthoredProperties Properties { get; init; }

    internal EntityApplier? Apply { get; init; }
}
