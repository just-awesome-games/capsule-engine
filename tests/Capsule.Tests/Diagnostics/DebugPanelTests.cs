using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Capsule.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Capsule.Tests.Diagnostics;

public sealed class DebugPanelTests
{
    private enum Mood
    {
        Idle,
        Alert,
    }

    // Written under a culture whose decimal separator is a comma, so the invariant spelling is
    // what the assertions prove; a float is not widened to double on the way through.
    [Fact]
    public void Fields_FormatInvariantAtShortestRoundTripAndNameEnumsAndNulls()
    {
        CultureInfo was = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            DebugPanel panel = new();

            panel.Field("Speed", 0.1f);
            panel.Field("Ratio", 0.1);
            panel.Field("Count", 12);
            panel.Field("At", new Vector2(120f, 64.5f));
            panel.Field("Mood", Mood.Alert);
            panel.Field("Target", (string?)null);
            panel.Field("Grounded", true);

            Assert.Equal(
                [
                    ("Speed", "0.1"),
                    ("Ratio", "0.1"),
                    ("Count", "12"),
                    ("At", "(120, 64.5)"),
                    ("Mood", "Alert"),
                    ("Target", "null"),
                    ("Grounded", "True"),
                ],
                Rows(panel));
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void ASection_IsAHeadingRowAndClearDropsEverything()
    {
        DebugPanel panel = new();

        panel.Field("Health", 3);
        panel.Section("Weapon");
        panel.Field("Ammo", 7);

        DebugPanelRow[] rows = panel.Rows.ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(DebugPanelRowKind.Field, rows[0].Kind);
        Assert.True(rows[1].IsHeading);
        Assert.Equal("Weapon", rows[1].Label);
        Assert.Null(rows[1].Value);

        panel.Clear();

        Assert.True(panel.Rows.IsEmpty);
    }

    // Rows keep write order across the verbs; a toggle's action carries the flip, so the overlay
    // never learns the setter.
    [Fact]
    public void CommandsAndToggles_InterleaveWithFieldsAndAToggleActivatesWithItsOpposite()
    {
        DebugPanel panel = new();
        int killed = 0;
        bool? set = null;

        panel.Field("Health", 3);
        panel.Command("Kill", () => killed++);
        panel.Toggle("Invulnerable", true, on => set = on);

        DebugPanelRow[] rows = panel.Rows.ToArray();
        Assert.Equal([DebugPanelRowKind.Field, DebugPanelRowKind.Command, DebugPanelRowKind.Toggle], rows.Select(static row => row.Kind));
        Assert.Equal("Kill", rows[1].Label);
        Assert.True(rows[2].On);

        rows[1].Activate!();
        rows[2].Activate!();

        Assert.Equal(1, killed);
        Assert.False(set);
    }

    [Fact]
    public void ANullLabelOrDelegate_Throws()
    {
        DebugPanel panel = new();

        Assert.Throws<ArgumentNullException>(() => panel.Field(null!, 1));
        Assert.Throws<ArgumentNullException>(() => panel.Command(null!, static () => { }));
        Assert.Throws<ArgumentNullException>(() => panel.Command("Kill", null!));
        Assert.Throws<ArgumentNullException>(() => panel.Toggle(null!, true, static _ => { }));
        Assert.Throws<ArgumentNullException>(() => panel.Toggle("Godmode", true, null!));
    }

    // A game hook compiled without the development symbol keeps no call into the panel and no
    // lambda: the closure the calls would have built is absent from the assembly, so a shipping
    // build carries nothing of them. The same source with the symbol keeps both.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHookCompiledWithoutTheDevelopmentSymbol_DropsEveryVerbAndItsLambda(bool development)
    {
        const string source = """
            using Capsule.Diagnostics;

            public sealed class Hook
            {
                private int _health = 3;
                private bool _godmode;

                public void OnDebugPanel(DebugPanel panel)
                {
                    panel.Field("Health", _health);
                    panel.Command("Kill", () => _health = 0);
                    panel.Toggle("Godmode", _godmode, on => _godmode = on);
                }
            }
            """;

        CSharpParseOptions parse = development
            ? new CSharpParseOptions(preprocessorSymbols: [Development.Symbol])
            : new CSharpParseOptions();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "HookSpecs",
            [CSharpSyntaxTree.ParseText(source, parse)],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        image.Position = 0;
        using PEReader pe = new(image);
        MetadataReader metadata = pe.GetMetadataReader();
        string[] referenced = [.. metadata.MemberReferences
            .Select(handle => metadata.GetMemberReference(handle))
            .Where(member => member.Parent.Kind == HandleKind.TypeReference
                && metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)member.Parent).Name) == nameof(DebugPanel))
            .Select(member => metadata.GetString(member.Name))
            .Distinct()
            .Order()];
        string[] lambdas = [.. metadata.MethodDefinitions
            .Select(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name))
            .Where(static name => name.StartsWith('<'))];

        if (development)
        {
            Assert.Equal(["Command", "Field", "Toggle"], referenced);
            Assert.Equal(2, lambdas.Length);
        }
        else
        {
            Assert.Empty(referenced);
            Assert.Empty(lambdas);
        }
    }

    private static ImmutableArray<MetadataReference> References()
    {
        string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        return
        [
            .. trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(static path => !Path.GetFileNameWithoutExtension(path).StartsWith("Capsule.", StringComparison.Ordinal))
                .Select(static path => MetadataReference.CreateFromFile(path)),
            MetadataReference.CreateFromFile(typeof(DebugPanel).Assembly.Location),
        ];
    }

    private static (string Label, string? Value)[] Rows(DebugPanel panel)
    {
        ReadOnlySpan<DebugPanelRow> rows = panel.Rows;
        (string, string?)[] pairs = new (string, string?)[rows.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            pairs[index] = (rows[index].Label, rows[index].Value);
        }

        return pairs;
    }
}
