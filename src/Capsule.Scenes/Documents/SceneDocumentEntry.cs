using System.Numerics;
using System.Text.Json;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes.Documents;

/// <summary>One entry in a scene document's ordered list: the entity class it places, where, and the members it authors.</summary>
/// <example>
/// An importer places a door at (96, 0) on band 3, which an entity reference names by id 4:
/// <code>
/// JsonElement members = JsonSerializer.SerializeToElement(new { destination = "scenes/hall" });
/// SceneDocumentEntry door = new("door", new EntitySpawn(new Vector2(96f, 0f)) { ZIndex = 3 }, members) { Id = 4 };
/// </code>
/// </example>
/// <param name="Type">The type key of the entity class the entry places, <c>tile-map</c> for the engine's map.</param>
/// <param name="Spawn">
/// Where and how the entry places its entity, which reaches the entity's constructor as authored. A
/// <c>default</c> spawn has a scale of zero, which the document refuses.
/// </param>
/// <param name="Members">
/// The entry's authorable member values as one JSON object, or null when it authors none. Each key sets the
/// entity class's authorable member of that name.
/// </param>
public readonly record struct SceneDocumentEntry(string Type, EntitySpawn Spawn, JsonElement? Members = null)
{
    // The members as one object, or null when the entry authors none.
    internal AuthoredObject? Authored { get; init; } = ObjectOf(Members);

    // Members an importer handed in that are not an object, kept for the document to refuse naming the entry.
    internal JsonElement? NotAnObject { get; init; } = Refused(Members);

    /// <summary>An entry placing its entity at the world origin, unturned and at scale one.</summary>
    /// <param name="type">The type key of the entity class the entry places.</param>
    /// <param name="members">The entry's authorable member values as one JSON object, or null when it authors none.</param>
    public SceneDocumentEntry(string type, JsonElement? members = null)
        : this(type, new EntitySpawn(Vector2.Zero), members)
    {
    }

    /// <summary>The id an entity reference names this entry by, or null when nothing references it.</summary>
    public int? Id { get; init; }

    /// <summary>The entry's authorable member values as one JSON object, or null when it authors none.</summary>
    /// <remarks>Each read builds a new object, which holds every key once.</remarks>
    public JsonElement? Members
    {
        get => Authored?.ToElement() ?? NotAnObject;
        init
        {
            Authored = ObjectOf(value);
            NotAnObject = Refused(value);
        }
    }

    private static AuthoredObject? ObjectOf(JsonElement? members) =>
        members is { ValueKind: JsonValueKind.Object } authored ? AuthoredObject.From(authored) : null;

    private static JsonElement? Refused(JsonElement? members) =>
        members is { ValueKind: not JsonValueKind.Object } ? members : null;
}
