using System.Text;
using Capsule.Build.Schemas;

namespace Capsule.Tests.Build;

/// <summary>The committed JSON Schemas under <c>schemas/</c> against the ones the parser classes generate.</summary>
public sealed class FormatSchemaTests
{
    [Fact]
    public void EachCommittedSchema_IsWhatItsParserGenerates()
    {
        string committed = Path.Combine(ToolWorkspace.Metadata("CapsuleCheckout"), "schemas");
        string fresh = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "schemas")).FullName;
        List<string> stale = [];
        foreach ((string file, string title, var parser) in FormatSchemas.Formats)
        {
            string generated = FormatSchemas.Generate(file, title, parser);
            string path = Path.Combine(committed, file);
            // Byte for byte, so a committed CRLF or BOM is stale too. The schemas are UTF-8 without a BOM.
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(generated)))
            {
                File.WriteAllText(Path.Combine(fresh, file), generated);
                stale.Add(file);
            }
        }

        // The package takes every file here, so one left behind by a removed format would still ship.
        string[] strays = [.. Directory.EnumerateFiles(committed, "*.schema.json").Select(Path.GetFileName)
            .Except(FormatSchemas.Formats.Select(static format => format.File)).Order(StringComparer.Ordinal)!];
        Assert.True(
            strays.Length == 0,
            $"schemas/{string.Join(", schemas/", strays)} is not generated from any parser class, and the package would still ship it. "
                + $"Delete it, or add its format to {nameof(FormatSchemas)}.{nameof(FormatSchemas.Formats)}.");

        Assert.True(
            stale.Count == 0,
            $"schemas/{string.Join(", schemas/", stale)} differ from what the parser classes generate. "
                + $"The schemas are generated, so a hand edit or a parser change alone makes them stale. Copy the fresh output from '{fresh}' over '{committed}' and commit it.");
    }
}
