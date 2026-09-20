using System.Collections.Immutable;
using Capsule.Generators;
using Capsule.Persistence;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Analyzers;

public sealed class SaveDocumentAnalyzerTests
{
    // The boundary fixtures strip every Capsule.* reference by default; this analyzer's contract is
    // about Capsule.Persistence.SaveKey<T> itself, so Capsule.Core comes back in.
    private static readonly ImmutableArray<MetadataReference> ExtraReferences =
        [MetadataReference.CreateFromFile(typeof(SaveKey<>).Assembly.Location)];

    [Fact]
    public async Task ANestedInitProperty_IsReportedOnce_AndASetSiblingIsClean()
    {
        ImmutableArray<Diagnostic> diagnostics = await Analyze("""
            public sealed record Item
            {
                public int Count { get; init; }
                public int Durability { get; set; }
            }

            public sealed record Save
            {
                public Item Piece { get; set; } = new();
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(SaveDocumentAnalyzer.InitOnlyPropertyId, diagnostic.Id);
        Assert.Contains("Item.Count", diagnostic.GetMessage());
    }

    // A required init property, a positional record, a metadata-type leaf (InputButton), and a
    // self-referencing type with a BCL generic leaf (List<int>): none of these is init-only save
    // state, so the walk that stops at the visited set reports nothing for any of them.
    private const string RequiredInit = """
        public sealed record Save
        {
            public required int Level { get; init; }
        }
        """;

    [Theory]
    [InlineData(RequiredInit, true, false)]
    [InlineData(RequiredInit, false, false)]
    [InlineData("public sealed record Save(int A, int B = default);", true, false)]
    [InlineData(
        """
        public sealed record Save
        {
            public InputButton Bound { get; set; }
        }
        """,
        true, false)]
    [InlineData(
        """
        public sealed class Save
        {
            public Save? Next { get; set; }
            public List<int> Tags { get; set; } = new();
        }
        """,
        true, false)]
    public async Task NothingReported(string types, bool logic, bool shell)
    {
        ImmutableArray<Diagnostic> diagnostics = await Analyze(types, logic, shell);

        Assert.Empty(diagnostics);
    }

    private static Task<ImmutableArray<Diagnostic>> Analyze(string types, bool logic = true, bool shell = false) =>
        GameBoundaryFixtures.Analyze(Document(types), logic, shell, ExtraReferences, new SaveDocumentAnalyzer());

    private static string Document(string types) =>
        $"""
        using System.Collections.Generic;
        using Capsule.Input;
        using Capsule.Persistence;

        namespace Game;

        {types}
        """ + """

        public static class Keys
        {
            public static readonly SaveKey<Save> Slot = new("slot", null!);
        }
        """;
}
