using System.Text.Json;

namespace Capsule.Tiles;

/// <summary>
/// What a scene document authors for one palette entry beyond the engine's fields: the <see cref="TileType"/>
/// subclass it composes and that class's authorable members. Both are null on a plain entry.
/// </summary>
/// <param name="Type">The key of the <see cref="TileType"/> subclass the entry composes, or null for a plain <see cref="TileType"/>.</param>
/// <param name="Properties">
/// The authored <c>properties</c> object, or null when the entry authors none. Each key sets the class's
/// authorable member of that name, camel-cased.
/// </param>
public readonly record struct AuthoredTileType(string? Type, JsonElement? Properties = null);
