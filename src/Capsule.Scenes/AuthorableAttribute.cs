namespace Capsule.Scenes;

/// <summary>Marks a field or property as one a scene document sets, on any class the document writes as a JSON object.</summary>
/// <remarks>
/// The key is the member's name camel-cased, a leading underscore dropped, beside the object's own reserved keys.
/// An entity's values land before the derived constructor body runs, and an entity reference once every entry is
/// built. A scene class's values land once every entry is built, before the derived constructor body runs. An
/// omitted key, like every member of a spawn built in code, keeps the initializer. A member with no setter that holds
/// an object is filled in place, and its object takes no <c>type</c> key. A key no member takes fails the scene at
/// load, and a member taking a key the document reserves fails the build. <c>docs/scenes.md</c> gives each member
/// type's JSON form.
/// </remarks>
/// <example>
/// <code>
/// public sealed class SceneExit : Component
/// {
///     [Authorable(Required = true)]
///     public SceneKey Destination { get; set; }
/// }
///
/// public sealed class Door : Entity
/// {
///     [Authorable]
///     public SceneExit Exit { get; } = new();
///
///     [Authorable]
///     private float _speed = 40f;
///
///     public Door(EntitySpawn spawn)
///         : base(spawn)
///     {
///         // Exit and _speed already hold the placement's values here.
///         Add(Exit);
///     }
/// }
/// </code>
/// The placement: <c>{ "type": "door", "x": 32, "speed": 60, "exit": { "destination": "scenes/hall" } }</c>.
/// </example>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class AuthorableAttribute : Attribute
{
    /// <summary>Whether every document object of the declaring class must author the member.</summary>
    /// <remarks>
    /// A mandatory entity reference, or array of them, on an entity or a scene class uses C#'s
    /// <see langword="required"/> instead. A class a document writes as an object inside an entry refuses
    /// <see langword="required"/>, and there Required marks an entity reference too, on a nullable member.
    /// </remarks>
    public bool Required { get; set; }
}
