using System.Text;
using System.Text.Json;
using Capsule.Build.Registry;
using Capsule.Scenes.Documents;

namespace Capsule.Build.Scenes;

/// <summary>
/// Writes a scene document's member: a <c>SceneKey</c> constant carrying the attributes the generator
/// composes the scene registry from and checks every entry by.
/// </summary>
internal static class SceneMembers
{
    // The key EntryStarts files the document's own properties under, which no entry id can take.
    private const int DocumentProperties = 0;

    // The generator checks every entry against the class claiming its type, so each member carries
    // what the generator cannot read out of the document itself.
    internal static void Write(StringBuilder code, string indent, string identifier, Source document, string[] attributes)
    {
        code.Append(indent).Append("/// <summary>The scene document <c>").Append(document.Key).AppendLine("</c>.</summary>");
        foreach (string attribute in attributes)
        {
            code.Append(indent).Append('[').Append(attribute).AppendLine("]");
        }

        code.Append(indent).Append("public static ").Append(GeneratedTypes.SceneKey).Append(' ').Append(identifier)
            .Append(" => new ").Append(GeneratedTypes.SceneKey).Append('(').Append(Literal.Of(document.Key)).AppendLine(");");
    }

    /// <summary>
    /// The attributes marking one document's key member: its key and settings, then one per game entry with
    /// where the entry starts in <paramref name="json"/>, the text of the file at <paramref name="path"/>.
    /// </summary>
    internal static List<string> Attributes(SceneDocument scene, string key, string path, string json)
    {
        List<string> named = [$"Key = {Literal.Of(key)}", $"Path = {Literal.Of(path)}"];
        if (scene.Settings.BaseScene is { } baseScene)
        {
            named.Add($"BaseScene = {Literal.Of(baseScene)}");
        }

        if (scene.Settings.Camera is { } camera)
        {
            named.Add($"Camera = {Literal.Of(camera)}");
        }

        if (scene.Source is { Tool: not SceneStep.ToolName } derived)
        {
            named.Add($"Source = {Literal.Of(derived.Path)}");
        }

        Dictionary<int, (int Line, int Column)> starts = EntryStarts(json);
        if (scene.Settings.Properties is { } authored)
        {
            named.Add($"Properties = new object?[] {{ {string.Join(", ", Pairs(authored))} }}");
            if (starts.TryGetValue(DocumentProperties, out (int Line, int Column) at))
            {
                named.Add($"Line = {Literal.Of(at.Line)}, Column = {Literal.Of(at.Column)}");
            }
        }

        List<string> attributes = [$"{GeneratedAttributes.SceneDocumentName}({string.Join(", ", named)})"];
        foreach (SceneDocumentEntry entry in scene.Entries)
        {
            if (entry.Entity is not { } placed)
            {
                continue;
            }

            StringBuilder placement = new StringBuilder(GeneratedAttributes.PlacementName)
                .Append('(').Append(Literal.Of(placed.Id)).Append(", ").Append(Literal.Of(placed.Type));
            if (placed.Properties is { } properties)
            {
                foreach (string pair in Pairs(properties))
                {
                    placement.Append(", ").Append(pair);
                }
            }

            if (starts.TryGetValue(placed.Id, out (int Line, int Column) start))
            {
                placement.Append(", Line = ").Append(Literal.Of(start.Line)).Append(", Column = ").Append(Literal.Of(start.Column));
            }

            attributes.Add(placement.Append(')').ToString());
        }

        return attributes;
    }

    // Each property's name and value as the constants an attribute carries.
    private static IEnumerable<string> Pairs(JsonElement properties) =>
        properties.EnumerateObject().Select(static property => Literal.Of(property.Name) + ", " + Constant(property.Value));

    // Where each entry's opening brace sits, by id, and the document's own properties' brace under
    // DocumentProperties, as the line and column an editor shows, both from 1.
    private static Dictionary<int, (int Line, int Column)> EntryStarts(string json)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        Utf8JsonReader reader = new(utf8);
        Dictionary<int, (int Line, int Column)> starts = [];
        bool inEntities = false;
        bool inProperties = false;
        (int Line, int Column) start = default;
        int line = 1;
        int lineStart = 0;
        int scanned = 0;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName when reader.CurrentDepth == 1:
                    inEntities = reader.ValueTextEquals("entities");
                    inProperties = reader.ValueTextEquals("properties");
                    break;

                case JsonTokenType.StartObject when (inEntities && reader.CurrentDepth == 2) || (inProperties && reader.CurrentDepth == 1):
                    int offset = (int)reader.TokenStartIndex;
                    for (; scanned < offset; scanned++)
                    {
                        if (utf8[scanned] == (byte)'\n')
                        {
                            line++;
                            lineStart = scanned + 1;
                        }
                    }

                    start = (line, 1 + Encoding.UTF8.GetCharCount(utf8, lineStart, offset - lineStart));
                    if (inProperties)
                    {
                        starts[DocumentProperties] = start;
                        inProperties = false;
                    }

                    break;

                case JsonTokenType.PropertyName when inEntities && reader.CurrentDepth == 3 && reader.ValueTextEquals("id"):
                    if (reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int id))
                    {
                        starts[id] = start;
                    }

                    break;
            }
        }

        return starts;
    }

    // A JSON value as the C# constant an attribute carries. Parsing rejected any number beyond double range.
    private static string Constant(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.TryGetInt32(out int whole) ? Literal.Of(whole) : Literal.Of(value.GetDouble()),
        JsonValueKind.String => Literal.Of(value.GetString()!),
        JsonValueKind.Array => "new object[] { " + string.Join(", ", value.EnumerateArray().Select(Constant)) + " }",
        JsonValueKind.Object => "typeof(object)",
        _ => "null",
    };
}
