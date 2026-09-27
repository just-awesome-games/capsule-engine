namespace Capsule.Scenes;

/// <summary>Marks an entity's field or property as one a scene document's placement may set.</summary>
/// <remarks>
/// <para>
/// Mark an instance field that is not <see langword="readonly"/>, or an instance property with a <c>set</c>
/// or <c>init</c> accessor, of any access. A member declared on a base class counts, and an override keeps
/// the mark of the member it overrides. The entry's <c>properties</c> key is the member's name camel-cased,
/// with a leading underscore dropped: <c>Speed</c> and <c>_speed</c> are both <c>"speed"</c>.
/// </para>
/// <para>
/// An authored value is set before the derived constructor body runs, like the position. A key the entry
/// omits leaves the member's initializer, as does every member of a spawn built in code.
/// </para>
/// <list type="table">
/// <listheader><term>Member type</term><description>JSON form</description></listheader>
/// <item><term><see langword="bool"/></term><description><c>true</c> or <c>false</c></description></item>
/// <item><term><see langword="int"/></term><description>a whole number in range</description></item>
/// <item><term><see langword="float"/></term><description>a finite number</description></item>
/// <item><term><see langword="string"/></term><description>a string</description></item>
/// <item>
/// <term>an enum without <see cref="FlagsAttribute"/></term>
/// <description>a member's name camel-cased: <c>IceCave</c> is <c>"iceCave"</c></description>
/// </item>
/// <item><term><see cref="System.Numerics.Vector2"/></term><description><c>[x, y]</c>, both finite</description></item>
/// <item>
/// <term><see cref="Capsule.Rendering.ColorRgba"/></term>
/// <description><c>"#rrggbb"</c> or <c>"#rrggbbaa"</c></description>
/// </item>
/// <item><term>a nullable of any of these</term><description>the same, or <c>null</c></description></item>
/// <item>
/// <term>a definition type</term>
/// <description>a definition's name camel-cased: <c>Default</c> is <c>"default"</c>. A value several placements share is a definition.</description>
/// </item>
/// <item>
/// <term>a type declaring <see cref="System.Text.Json.Serialization.JsonConverterAttribute"/></term>
/// <description>the form its converter reads</description>
/// </item>
/// </list>
/// <para>
/// A definition type is a <see langword="readonly"/> struct, or a record class without settable members.
/// Its <c>public static readonly</c> fields of its own type are its definitions, and every placement naming
/// one shares that instance.
/// </para>
/// <para>
/// A converter is a <c>JsonConverter&lt;T&gt;</c> of exactly the member's type, visible to the game's
/// assembly, with a public parameterless constructor. A type the table covers keeps its table form. The
/// build checks every placement against its class, and a converter's value is checked when the scene loads.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class Door : Entity
/// {
///     [Authorable(Required = true)]
///     public string Target { get; private set; } = string.Empty;
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
/// The placement: <c>{ "id": 7, "type": "door", "x": 32, "y": 0, "properties": { "target": "hall", "speed": 60 } }</c>.
/// </example>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class AuthorableAttribute : Attribute
{
    /// <summary>Whether every document placement must author the member.</summary>
    /// <remarks>
    /// A spawn built in code still leaves the initializer. Give a required member of a reference type a
    /// usable one, such as <c>string.Empty</c>. Use this rather than C#'s <see langword="required"/>
    /// modifier, which the generated constructor call cannot satisfy.
    /// </remarks>
    public bool Required { get; set; }
}
