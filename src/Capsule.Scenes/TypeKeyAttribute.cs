namespace Capsule.Scenes;

/// <summary>
/// Overrides the type key a class would otherwise derive from its namespace. A scene document's <c>type</c>
/// names an entity, a camera, a tile type or any member object's subclass by this key. The value is a complete
/// key: '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows
/// device name.
/// </summary>
/// <example>
/// <code>
/// [TypeKey("door")]
/// public sealed class LockedDoor(EntitySpawn spawn) : Entity(spawn);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TypeKeyAttribute : Attribute
{
    /// <param name="key">The type key this class claims in place of the key its namespace names.</param>
    public TypeKeyAttribute(string key) => Key = key;

    /// <summary>The type key this class claims.</summary>
    public string Key { get; }
}
