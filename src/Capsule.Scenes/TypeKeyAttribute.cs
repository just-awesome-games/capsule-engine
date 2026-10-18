namespace Capsule.Scenes;

/// <summary>
/// Overrides the key a class would otherwise derive from its namespace. It applies to any class a scene document's
/// <c>type</c> or <c>baseScene</c> names, and on a scene taking <see cref="SceneContent"/> it names the document that
/// scene claims.
/// </summary>
/// <remarks>
/// Each '/'-joined segment of the value reduces to the kebab form of the identifier it names, as a document's key
/// does: "Bosses/Wyrm" claims "bosses/wyrm".
/// </remarks>
/// <example>
/// <code>
/// [TypeKey("door")]
/// public sealed class LockedDoor(EntitySpawn spawn) : Entity(spawn);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TypeKeyAttribute : Attribute
{
    /// <summary>The type key this class claims.</summary>
    public string Key { get; }

    /// <param name="key">The type key this class claims in place of the key its namespace names.</param>
    public TypeKeyAttribute(string key) => Key = key;
}
