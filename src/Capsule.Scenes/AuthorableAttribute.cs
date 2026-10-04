namespace Capsule.Scenes;

/// <summary>Marks a field or property as one a scene document sets, on any class the document writes as a JSON object.</summary>
/// <remarks>
/// <para>
/// The key is the member's name camel-cased, a leading underscore dropped, beside the object's own reserved keys.
/// An entity's values land before the derived constructor body runs, and an entity reference once every entry is
/// built. A scene class's values land once every entry is built, before the derived constructor body runs. An
/// omitted key, like every member of a spawn built in code, keeps the initializer. A key no member takes fails the
/// scene at load, and a member taking a key the document reserves fails the build.
/// </para>
/// <list type="table">
/// <listheader><term>Member type</term><description>JSON form</description></listheader>
/// <item>
/// <term><see langword="bool"/>, <see langword="int"/>, <see langword="float"/>, <see langword="string"/></term>
/// <description>the JSON value</description>
/// </item>
/// <item>
/// <term><see cref="System.Numerics.Vector2"/>, <see cref="Capsule.Rendering.ColorRgba"/></term>
/// <description><c>[x, y]</c>; <c>"#rrggbb"</c> or <c>"#rrggbbaa"</c></description>
/// </item>
/// <item>
/// <term><see cref="Capsule.Rendering.Rect"/></term>
/// <description>its edges, <c>[left, top, right, bottom]</c>, with right no less than left and bottom no less than top</description>
/// </item>
/// <item>
/// <term>an enum</term>
/// <description>
/// a member's name camel-cased, <c>"iceCave"</c>; a <see cref="FlagsAttribute"/> enum's joined by commas,
/// <c>"spikes, fire"</c>
/// </description>
/// </item>
/// <item>
/// <term><see cref="Capsule.Assets.TextureHandle"/>, <see cref="Capsule.Audio.AudioClip"/></term>
/// <description>key and extension, <c>"textures/hazard.png"</c>, preloaded with the scene</description>
/// </item>
/// <item><term><see cref="SceneKey"/></term><description>the document's key, <c>"scenes/halls/hall"</c></description></item>
/// <item>
/// <term>an <see cref="Entity"/> subclass or interface</term>
/// <description>the target entry's id, readable from <see cref="Entity.OnStart"/> on</description>
/// </item>
/// <item>
/// <term>a definition: a <see langword="readonly"/> struct or immutable record with <c>public static readonly</c> instances</term>
/// <description>an instance's name camel-cased, <c>"default"</c>, shared by every placement naming it</description>
/// </item>
/// <item>
/// <term>a type with <see cref="System.Text.Json.Serialization.JsonConverterAttribute"/></term>
/// <description>its converter's form, checked at load</description>
/// </item>
/// <item>
/// <term>a class declaring <see cref="AuthorableAttribute"/> members</term>
/// <description>
/// an object of them, <c>{ "speed": 30 }</c>, filling the instance the member holds or else a new one. Its
/// <c>"type"</c> key names a subclass to construct in its place, keyed as an entity class is. A member with no
/// setter is filled in place and takes no type. Generated code constructs the class, so C#'s
/// <see langword="required"/> is refused on its members and <see cref="Required"/> marks a nullable reference.
/// </description>
/// </item>
/// <item><term><c>T[]</c> of any of these</term><description><c>[a, b, c]</c></description></item>
/// <item><term>a nullable of any of these</term><description>also <c>null</c></description></item>
/// </list>
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
    /// <see langword="required"/> instead.
    /// </remarks>
    public bool Required { get; set; }
}
