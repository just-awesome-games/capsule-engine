using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Build.Registry;
using Capsule.Scenes.Documents;
using Capsule.Tiles;

namespace Capsule.Build.Scenes;

/// <summary>
/// Every scene document, authored or derived by a module, validated and re-emitted compact and gzipped
/// where it ships, and each key declared as a constant. The indented canonical form is the authoring format.
/// </summary>
internal static class SceneStep
{
    /// <summary>The tool a hand-authored document's provenance names.</summary>
    internal const string ToolName = "native";

    private const char ByteOrderMark = '\uFEFF';

    // What marks a constant as a shipped scene document, and each of its game entries. The generator
    // finds them by these names. Both are conditional on a symbol no build defines, so the compiler reads
    // them from source and leaves them out of the assembly.
    internal const string DocumentAttributeName = "CapsuleGeneratedSceneDocument";

    internal const string PlacementAttributeName = "CapsuleGeneratedPlacement";

    internal const string DocumentAttribute = """
            /// <summary>A shipped scene document, with the settings the build read from it. Generated code.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Field)]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            [global::System.Diagnostics.Conditional("CAPSULE_GENERATED_FACTS")]
            internal sealed class CapsuleGeneratedSceneDocumentAttribute : global::System.Attribute
            {
                /// <summary>The key of the abstract scene the document derives from, or null.</summary>
                public string? BaseScene { get; set; }

                /// <summary>The key of the camera the document installs, or null.</summary>
                public string? Camera { get; set; }

                /// <summary>The file an authoring module derived the document from, or null for a hand-authored one.</summary>
                public string? Source { get; set; }

                /// <summary>The file the build read, relative to the project, where the compiler reports an entry's error.</summary>
                public string? Path { get; set; }
            }

            /// <summary>One game entry of a shipped scene document, which the compiler checks against the class claiming its type. Generated code.</summary>
            /// <remarks>
            /// Each property is its name, then its JSON value as a C# constant: a bool, an int, a double, a string,
            /// null or an object array. A JSON object is written typeof(object), since only a converter reads one.
            /// </remarks>
            [global::System.AttributeUsage(global::System.AttributeTargets.Field, AllowMultiple = true)]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            [global::System.Diagnostics.Conditional("CAPSULE_GENERATED_FACTS")]
            internal sealed class CapsuleGeneratedPlacementAttribute : global::System.Attribute
            {
                /// <summary>The entry's id, type, and each property's name and value in turn.</summary>
                public CapsuleGeneratedPlacementAttribute(int id, string type, params object?[] properties)
                {
                }

                /// <summary>The line the entry starts on in the file the build read, counted from 1.</summary>
                public int Line { get; set; }

                /// <summary>The column the entry starts at on that line, counted from 1.</summary>
                public int Column { get; set; }
            }

        """;

    internal static void Build(BuildPass pass)
    {
        List<(Source Source, SceneDocument Document)> documents = pass.Each(
            pass.Of(AssetType.Scenes),
            source =>
            {
                SceneDocument document = Import(source.Path, pass.Requests.TileSize);
                string shipped = pass.Shipped.Claim(source.Key + ShippedSceneDocument.Extension);
                AtomicFile.Write(shipped, path => ShippedSceneDocument.Write(document, path));
                pass.Output.WriteLine($"scenes: {source.Path} -> {source.Key}");

                return document;
            });

        if (documents.Count > 0)
        {
            pass.Assets.Beside(DocumentAttribute);
        }

        // The generator composes the scene registry from these constants and checks every entry against
        // the class claiming its type, so each carries what it cannot read out of the document itself.
        foreach ((Source document, SceneDocument scene) in documents)
        {
            List<string> attributes = Attributes(scene, document.Path, File.ReadAllText(document.Path));

            pass.Declare(document, (source, indent, identifier) =>
            {
                source.Append(indent).Append("/// <summary>The scene document <c>").Append(document.Key).AppendLine("</c>.</summary>");
                foreach (string attribute in attributes)
                {
                    source.Append(indent).Append('[').Append(attribute).AppendLine("]");
                }

                source.Append(indent).Append("public const string ").Append(identifier).Append(" = ").Append(Literal.Of(document.Key)).AppendLine(";");
            });
        }
    }

    /// <summary>
    /// The attributes marking one document's key constant: its settings, then one per game entry with where the
    /// entry starts in <paramref name="json"/>, the text of the file at <paramref name="path"/>.
    /// </summary>
    internal static List<string> Attributes(SceneDocument scene, string path, string json)
    {
        List<string> named = [$"Path = {Literal.Of(path)}"];
        if (scene.Settings.BaseScene is { } baseScene)
        {
            named.Add($"BaseScene = {Literal.Of(baseScene)}");
        }

        if (scene.Settings.Camera is { } camera)
        {
            named.Add($"Camera = {Literal.Of(camera)}");
        }

        if (scene.Source is { Tool: not ToolName } derived)
        {
            named.Add($"Source = {Literal.Of(derived.Path)}");
        }

        List<string> attributes = [$"{DocumentAttributeName}({string.Join(", ", named)})"];
        Dictionary<int, (int Line, int Column)> starts = EntryStarts(json);
        foreach (SceneDocumentEntry entry in scene.Entries)
        {
            if (entry.Entity is not { } placed)
            {
                continue;
            }

            StringBuilder placement = new StringBuilder(PlacementAttributeName)
                .Append('(').Append(Literal.Of(placed.Id)).Append(", ").Append(Literal.Of(placed.Type));
            if (placed.Properties is { } properties)
            {
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    placement.Append(", ").Append(Literal.Of(property.Name)).Append(", ").Append(Constant(property.Value));
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

    // Where each entry's opening brace sits, by id, as the line and column an editor shows, both from 1.
    private static Dictionary<int, (int Line, int Column)> EntryStarts(string json)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        Utf8JsonReader reader = new(utf8);
        Dictionary<int, (int Line, int Column)> starts = [];
        bool inEntities = false;
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
                    break;

                case JsonTokenType.StartObject when inEntities && reader.CurrentDepth == 2:
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

    internal static SceneDocument Import(string documentPath, int? tileSize = null)
    {
        // Read as bytes, since the hash is over the source bytes and an authored file need not be
        // canonical. The format is written without a BOM, but an editor may add one and the JSON
        // reader would find it where it expects a brace.
        byte[] sourceBytes = File.ReadAllBytes(documentPath);
        SceneDocument authored = SceneDocumentFile.Parse(Encoding.UTF8.GetString(sourceBytes).TrimStart(ByteOrderMark));

        if (tileSize is { } declared)
        {
            foreach (SceneDocumentEntry entry in authored.Entries)
            {
                if (entry.TileMap is { Grid.TileSize: var actual } && actual != declared)
                {
                    throw new SceneDocumentFormatException(
                        $"the scene document has {actual}px tiles but the game declares {declared}px. Set tileSize to {declared} on every tile-map entry, or change CapsuleTileSize.");
                }
            }
        }

        // A document that arrives stamped was derived by an authoring module, and its block names
        // the file a person edited. Re-stamping it would name the intermediate instead.
        SceneDocumentSource source = authored.Source ?? new(
            ToolName,
            documentPath.Replace('\\', '/'),
            Convert.ToHexStringLower(SHA256.HashData(sourceBytes)));

        return new SceneDocument(Keyed(authored.Entries), authored.NextEntityId, source, authored.Settings);
    }

    // A texture is reached by its key however the document spelled it, so what is re-emitted and
    // what the runtime loads is the path the build ships it at.
    private static SceneDocumentEntry[] Keyed(ReadOnlySpan<SceneDocumentEntry> entries)
    {
        SceneDocumentEntry[] keyed = new SceneDocumentEntry[entries.Length];

        for (int i = 0; i < entries.Length; i++)
        {
            keyed[i] = entries[i].TileMap is { Grid: { Texture: { } texture } grid } map
                ? new TileMapPlacement(map.Id, Regrid(grid, Keyed(texture)), map.ZIndex, map.ScrollFactor)
                : entries[i];
        }

        return keyed;
    }

    private static TextureHandle Keyed(TextureHandle texture) =>
        new(
            Keys.Of(texture.Name, $"has a tile-map entry drawing from texture \"{texture.Name}{texture.Extension}\""),
            texture.Extension.ToLowerInvariant());

    private static TileGrid Regrid(TileGrid grid, TextureHandle texture) =>
        texture == grid.Texture
            ? grid
            : new TileGrid(
                grid.TileSize,
                grid.Width,
                grid.Height,
                grid.TileTypes.ToArray(),
                grid.Tiles.ToArray(),
                texture,
                grid.Columns,
                grid.Transforms.ToArray());
}
