using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Capsule.Assets;

namespace Capsule.Scenes.Documents;

// The file shape, mapped one to one onto the JSON. Declaration order is the written order, so reordering a
// member here changes every written document's bytes.
// Every member is nullable where the reader must tell an omitted field from a present one, so it can
// name the defect instead of reading a default. Each object's undeclared keys are its class's
// [Authorable] members, which the generated applier reads and refuses when no member takes one.
[Description("A scene document: every entity a scene places. Any other key sets the member the composing scene class marks [Authorable], the engine's own Scene members among them.")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed class SceneDocumentJson
{
    // Read and ignored. The writer never sets it, so no written or shipped document carries it.
    [JsonPropertyName(SchemaKeyConverter.Key)]
    [JsonConverter(typeof(SchemaKeyConverter))]
    public string? Schema { get; set; }

    [Description("The key of an abstract Scene subclass. The composed scene derives from it. Absent composes a plain Scene.")]
    public string? BaseScene { get; set; }

    // A null entry reaches the reader, which names it. An initializer here would invent data the format
    // never accepted.
    [Description("Every entry the scene places, in document order. Write an empty list for a scene with nothing in it.")]
    [Required]
    public SceneEntryJson?[]? Entities { get; set; }

    // The writer moves these ahead of entities.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Members { get; set; }
}

[Description("One entry: the entity it places. Any other key sets the member the entity's class marks [Authorable].")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed class SceneEntryJson
{
    [Description("The entry's id, which an entity reference names: positive and unique in the document. Absent on an entry nothing references.")]
    [Range(1, int.MaxValue)]
    public int? Id { get; set; }

    [Description("The spawn type: the key of the entity class the entry composes, \"tile-map\" for the engine's tile map.")]
    [Required]
    [SchemaLength(1)]
    public string? Type { get; set; }

    [Description("The entry's x position. Absent is 0.")]
    [DefaultValue(0f)]
    public float? X { get; set; }

    [Description("The entry's y position. Absent is 0.")]
    [DefaultValue(0f)]
    public float? Y { get; set; }

    [Description("The turn in degrees, clockwise on screen. Absent is 0.")]
    [DefaultValue(0f)]
    public float? Rotation { get; set; }

    [Description("The raw authored factor as [x, y], both finite and greater than zero. Absent is identity.")]
    [SchemaLength(2, 2)]
    [Range(0d, double.MaxValue, MinimumIsExclusive = true)]
    public float[]? Scale { get; set; }

    // An authored 0 is an ordinary band and is written back, so it stays distinct from an absent field.
    [Description("The entry's draw band. Absent keeps the band the entity's class sets.")]
    public int? ZIndex { get; set; }

    [Description("How far the entry moves with the camera, as [x, y], both finite. Absent keeps the factor the entity's class sets.")]
    [SchemaLength(2, 2)]
    public float[]? ScrollFactor { get; set; }

    // Read key by key into the entity class's authorable members when it spawns.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Members { get; set; }
}
