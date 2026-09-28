using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Capsule.Assets;
using Capsule.Build.Configuration;
using Capsule.Build.Sheets;
using Capsule.Build.Textures;
using Capsule.Scenes.Documents;

namespace Capsule.Build.Schemas;

/// <summary>Writes the JSON Schema of each authored file format from the classes that parse it.</summary>
/// <remarks>
/// The files under <c>schemas/</c> are this output, committed. A test holds them equal to it. A member's
/// <c>[Description]</c> is its text, and its <c>[Required]</c>, <c>[Range]</c>, <c>[SchemaLength]</c>,
/// <c>[RegularExpression]</c>, <c>[AllowedValues]</c> and <c>[DefaultValue]</c> become the matching keywords.
/// The parsers do not read those attributes. Each reader enforces its own rules.
/// </remarks>
internal static class FormatSchemas
{
    /// <summary>A name that becomes a C# member or one key segment: letters, digits, '-' and '_', starting with a letter after any '-' or '_'.</summary>
    internal const string NamePattern = "^[-_]*[A-Za-z][A-Za-z0-9_-]*$";

    private const string Dialect = "http://json-schema.org/draft-07/schema#";

    private const string BaseUrl = "https://raw.githubusercontent.com/just-awesome-games/capsule-engine/main/schemas/";

    private const string AtlasNameDescription =
        "An atlas name: letters, digits, '-' and '_', starting with a letter after any '-' or '_'. It folds case the way a key does.";

    private const string SchemaKeyDescription =
        "The URL of this file's JSON Schema, which gives an editor completion and hover. The build ignores its value.";

    /// <summary>Every authored file format: its schema's file name under <c>schemas/</c>, its title and the root class that parses it.</summary>
    internal static readonly (string File, string Title, JsonTypeInfo Parser)[] Formats =
    [
        ("folder.config.schema.json", "Capsule folder config", AssetConfigJsonContext.Default.FolderConfigJson),
        ("texture.config.schema.json", "Capsule texture sidecar", AssetConfigJsonContext.Default.TextureSidecarJson),
        ("atlas.schema.json", "Capsule atlas", AssetConfigJsonContext.Default.AtlasConfigJson),
        ("sheet.schema.json", "Capsule sprite sheet", SheetJsonContext.Default.SheetJson),
        ("scene.schema.json", "Capsule scene document", SceneDocumentJsonContext.Default.SceneDocumentJson),
    ];

    private static readonly JsonSchemaExporterOptions Exporter = new() { TransformSchemaNode = Transform };

    /// <summary>The schema of the file <paramref name="parser"/> reads, as LF-terminated indented JSON.</summary>
    internal static string Generate(string file, string title, JsonTypeInfo parser)
    {
        JsonObject exported = parser.GetJsonSchemaAsNode(Exporter).AsObject();

        JsonObject root = new()
        {
            ["$schema"] = Dialect,
            ["$id"] = BaseUrl + file,
            ["$comment"] = $"Generated from {parser.Type.FullName}.",
            ["title"] = title,
            ["description"] = DescriptionOf(parser.Type),
            ["type"] = "object",
            ["properties"] = new JsonObject { [SchemaKeyConverter.Key] = new JsonObject { ["description"] = SchemaKeyDescription, ["type"] = "string" } },
        };

        JsonObject properties = root["properties"]!.AsObject();
        foreach ((string name, JsonNode? setting) in Detach(exported["properties"]!.AsObject()))
        {
            // A parser that reads the key itself declares it. Its schema is the one above either way.
            if (name != SchemaKeyConverter.Key)
            {
                properties.Add(name, setting);
            }
        }

        if (exported["required"] is JsonArray required)
        {
            exported.Remove("required");
            root["required"] = required;
        }

        root["additionalProperties"] = false;

        using MemoryStream output = new();
        using (Utf8JsonWriter writer = new(output, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            root.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(output.ToArray()) + "\n";
    }

    // Runs once per node. A member's node arrives with its property, a file root or an array item without one.
    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode node)
    {
        Type type = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;

        // C# null means absent. A written null is refused, or read as absent and never written.
        if (node is JsonObject schema && schema["type"] is JsonArray types)
        {
            JsonNode[] kept = [.. types.Where(static kind => kind!.GetValue<string>() != "null").Select(static kind => kind!.DeepClone())];
            schema["type"] = kept.Length == 1 ? kept[0] : new JsonArray(kept);
        }

        if (node is JsonObject shape && context.TypeInfo.Kind == JsonTypeInfoKind.Object)
        {
            Require(shape, context.TypeInfo);
            if (type == typeof(SceneEntryJson))
            {
                TileMapProperties(shape);
            }
        }

        if (context.PropertyInfo is not { } property || property.Name == SchemaKeyConverter.Key)
        {
            return node;
        }

        MemberInfo member = property.AttributeProvider as MemberInfo
            ?? throw new InvalidOperationException($"The member \"{property.Name}\" exposes no C# member to read its description from. Declare it as a property of its parser class.");

        RefuseTrimUnsafeLength(member);
        JsonObject setting = new() { ["description"] = DescriptionOf(member) };
        if (member.GetCustomAttribute<DefaultValueAttribute>() is { Value: { } value })
        {
            setting["default"] = Spell(value, context.TypeInfo.Options);
        }

        if (type == typeof(AtlasSetting))
        {
            setting["default"] = Spell(default(AtlasSetting), context.TypeInfo.Options);
            setting["oneOf"] = new JsonArray(
                new JsonObject { ["const"] = false },
                new JsonObject { ["description"] = AtlasNameDescription, ["type"] = "string", ["pattern"] = NamePattern });
            return setting;
        }

        if (type.IsEnum)
        {
            setting["default"] = Spell(Activator.CreateInstance(type)!, context.TypeInfo.Options);
            setting["enum"] = new JsonArray([.. Enum.GetValues(type).Cast<object>().Select(member => Spell(member, context.TypeInfo.Options))]);
            return setting;
        }

        // Raw JSON whose contract belongs to someone else, such as a game entity's properties.
        if (type == typeof(JsonElement))
        {
            setting["type"] = "object";
            return setting;
        }

        if (node is not JsonObject exported)
        {
            throw new InvalidOperationException(
                $"The member \"{property.Name}\" has the custom converter {context.TypeInfo.Converter.GetType().Name}, which {nameof(FormatSchemas)} does not map. Map its converter type in {nameof(Transform)}.");
        }

        foreach ((string keyword, JsonNode? content) in Detach(exported))
        {
            setting.Add(keyword, content);
        }

        Constrain(setting, member);
        return setting;
    }

    // The keywords a member's validation attributes stand for. A value rule on an array applies to its items.
    private static void Constrain(JsonObject setting, MemberInfo member)
    {
        bool array = setting["type"]?.GetValue<string>() == "array";
        JsonObject values = array ? setting["items"]!.AsObject() : setting;

        if (member.GetCustomAttribute<SchemaLengthAttribute>() is { } length)
        {
            setting[array ? "minItems" : "minLength"] = length.Minimum;
            if (length.Maximum != int.MaxValue)
            {
                setting[array ? "maxItems" : "maxLength"] = length.Maximum;
            }
        }

        if (member.GetCustomAttribute<RangeAttribute>() is { } range)
        {
            values[range.MinimumIsExclusive ? "exclusiveMinimum" : "minimum"] = Literal(range.Minimum);

            // The type's largest value stands for no upper bound.
            if (range.Maximum is not (int.MaxValue or double.MaxValue))
            {
                values[range.MaximumIsExclusive ? "exclusiveMaximum" : "maximum"] = Literal(range.Maximum);
            }
        }

        if (member.GetCustomAttribute<RegularExpressionAttribute>() is { } pattern)
        {
            values["pattern"] = pattern.Pattern;
        }

        if (member.GetCustomAttribute<AllowedValuesAttribute>() is { Values: var allowed })
        {
            JsonNode[] spelled = [.. allowed.Select(static value => Literal(value!))];
            if (spelled.Length == 1)
            {
                values["const"] = spelled[0];
            }
            else
            {
                values["enum"] = new JsonArray(spelled);
            }
        }
    }

    // An object's members marked [Required], by their JSON names in declaration order.
    private static void Require(JsonObject shape, JsonTypeInfo type)
    {
        JsonArray required = [.. type.Properties
            .Where(static property => property.AttributeProvider is MemberInfo member && member.IsDefined(typeof(RequiredAttribute)))
            .Select(static property => (JsonNode)property.Name)];
        if (required.Count > 0)
        {
            shape["required"] = required;
        }
    }

    // A tile-map entry sits at the origin, unturned and unscaled, with a grid as its properties. Any other
    // entry's properties belong to its class, and no schema describes them.
    private static void TileMapProperties(JsonObject entry)
    {
        JsonObject grid = SceneDocumentJsonContext.Default.TileGridJson.GetJsonSchemaAsNode(Exporter).AsObject();
        grid.Insert(0, "description", DescriptionOf(typeof(TileGridJson)));

        entry["if"] = new JsonObject
        {
            ["properties"] = new JsonObject { ["type"] = new JsonObject { ["const"] = SceneDocument.TileMapType } },
            ["required"] = new JsonArray("type"),
        };
        entry["then"] = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["x"] = new JsonObject { ["const"] = 0 },
                ["y"] = new JsonObject { ["const"] = 0 },
                ["rotation"] = false,
                ["scale"] = false,
                ["properties"] = grid,
            },
            ["required"] = new JsonArray("properties"),
        };
    }

    private static string DescriptionOf(MemberInfo member) =>
        member.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? throw new InvalidOperationException($"{member.DeclaringType?.Name}.{member.Name} has no [Description]. Add one: it is the text an editor shows for it.");

    // A trimmed publish refuses these attributes' constructors, and the scene document ships in every game.
    private static void RefuseTrimUnsafeLength(MemberInfo member)
    {
        if (member.IsDefined(typeof(LengthAttribute)) || member.IsDefined(typeof(MinLengthAttribute)) || member.IsDefined(typeof(MaxLengthAttribute)))
        {
            throw new InvalidOperationException(
                $"{member.DeclaringType?.Name}.{member.Name} carries a DataAnnotations length attribute, which fails a NativeAOT publish. Use [SchemaLength] instead.");
        }
    }

    // A value as a file spells it.
    private static JsonNode Spell(object value, JsonSerializerOptions options) => value switch
    {
        int or float or double or bool or string => Literal(value),
        _ => JsonSerializer.SerializeToNode(value, options.GetTypeInfo(value.GetType()))!,
    };

    private static JsonNode Literal(object value) => value switch
    {
        int whole => whole,
        float single => single,
        double number => number,
        bool flag => flag,
        string text => text,
        _ => throw new InvalidOperationException($"{nameof(FormatSchemas)} has no JSON literal for a {value.GetType().Name}. Spell it in {nameof(Literal)}."),
    };

    // Removes and returns every member of a node, in order, so each can move to another parent.
    private static KeyValuePair<string, JsonNode?>[] Detach(JsonObject node)
    {
        KeyValuePair<string, JsonNode?>[] members = [.. node];
        node.Clear();
        return members;
    }
}
