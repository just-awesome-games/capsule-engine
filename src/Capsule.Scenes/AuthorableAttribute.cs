namespace Capsule.Scenes;

/// <summary>Marks an entity's or a scene class's field or property as one a scene document sets.</summary>
/// <remarks>
/// <para>
/// The entry's <c>properties</c> key is the member's name camel-cased, a leading underscore dropped. A
/// value lands before the derived constructor body runs, and an entity reference once every entry is
/// built. An omitted key, like every member of a spawn built in code, keeps the initializer. The build
/// checks each placement and names what a member cannot take.
/// </para>
/// <para>
/// A scene class's members are set the same way by the document's top-level <c>properties</c>, once every
/// entry is built and before the derived constructor body runs.
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
/// <item><term><c>T[]</c> of any of these</term><description><c>[a, b, c]</c></description></item>
/// <item><term>a nullable of any of these</term><description>also <c>null</c></description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public sealed class Door : Entity
/// {
///     [Authorable(Required = true)]
///     public SceneKey Target { get; private set; }
///
///     [Authorable]
///     private float _speed = 40f;
///
///     public Door(EntitySpawn spawn)
///         : base(spawn)
///     {
///         // Target and _speed already hold the placement's values here.
///     }
/// }
/// </code>
/// The placement: <c>{ "id": 7, "type": "door", "x": 32, "y": 0, "properties": { "target": "scenes/hall", "speed": 60 } }</c>.
/// </example>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class AuthorableAttribute : Attribute
{
    /// <summary>Whether every document placement, or every document composing the scene class, must author the member.</summary>
    /// <remarks>A mandatory entity reference, or array of them, uses C#'s <see langword="required"/> instead.</remarks>
    public bool Required { get; set; }
}
