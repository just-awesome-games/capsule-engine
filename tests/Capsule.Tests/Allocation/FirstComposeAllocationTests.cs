using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Generators;
using Capsule.Tiles;

namespace Capsule.Tests.Allocation;

// A level's first load parses its document and composes it, and no earlier load paid for either. Each test loads
// one document first, which keeps compiling the code out of the count, and then measures a second, new document.
[Collection(StageAllocationCollection.Name)]
public sealed class FirstComposeAllocationTests(ITestOutputHelper output)
{
    private const int Entries = 1_000;
    private const int TileTypes = 1_000;

    private const string Game = """
        #nullable enable
        using System.Numerics;
        using Capsule.Rendering;
        using Capsule.Scenes;

        namespace Game;

        public sealed class Marker(EntitySpawn spawn) : Entity(spawn)
        {
            [Authorable(Required = true)]
            public Vector2 Size { get; private set; }

            [Authorable]
            public int Period { get; private set; }

            [Authorable]
            public ColorRgba Glow { get; private set; }

            [Authorable]
            public Marker? Next { get; set; }
        }
        """;

    private static readonly Lazy<SceneRegistry> Registry = new(static () => GeneratorHarness.Registry(GeneratorHarness.Compile(Game)));

    // An entry authoring the same keys as the one before it shares its array of them, and the entries' reads note
    // what they asked for in tables their composition shares. What is left is its values, its entity, its placement,
    // and one slot each for its keys and its reference until the composition finishes.
    [Fact]
    public void LoadingADocumentOfAuthoredEntries_AllocatesUnderABudgetAnEntry()
    {
        StringBuilder entries = new();
        for (int index = 1; index <= Entries; index++)
        {
            entries.Append(CultureInfo.InvariantCulture, $$"""{"id": {{index}}, "type": "marker", "x": {{index}}, "y": 8, "size": [16, 24], "period": {{index % 90}}, "glow": "#ff8000", "next": {{(index % Entries) + 1}}},""");
        }

        string document = $$"""{"entities": [{{entries.ToString().TrimEnd(',')}}]}""";

        Report("a document of authored entries", document, Entries, budget: Entries * 1_300);
    }

    // A palette's entries that author the same keys in a row share one array of them, and a map reads the palette
    // once.
    [Fact]
    public void LoadingAPalette_AllocatesUnderABudgetATileType()
    {
        string palette = string.Join(", ", Enumerable.Range(1, TileTypes).Select(static index => $$"""{"name": "t{{index}}", "layer": "solid", "oneWay": true}"""));
        string document = $$"""{"entities": [{"type": "tile-map", "tileSize": 16, "width": 1, "height": 1, "tileTypes": [{"name": "empty"}, {{palette}}], "tiles": [1]}]}""";

        Report("a palette of tile types", document, TileTypes, budget: TileTypes * 750);
    }

    // The layers of one level author one palette, which the document reads once. What the palette makes, its
    // sprites and shapes, is cut once too.
    [Fact]
    public void MapsAuthoringOnePalette_ShareWhatItMakes()
    {
        const string Map = """{"type": "tile-map", "tileSize": 16, "width": 2, "height": 1, "tileTypes": [{"name": "empty"}, {"name": "ground", "layer": "solid", "shape": [[0, 16], [16, 0], [16, 16]]}], "tiles": [0, 1]}""";

        TileMap[] maps = [.. Load($$"""{"entities": [{{Map}}, {{Map}}]}""").Entities.ToArray().OfType<TileMap>()];

        Assert.True(MemoryMarshal.TryGetArray(maps[0].Grid.Sprites, out ArraySegment<Sprite?> first));
        Assert.True(MemoryMarshal.TryGetArray(maps[1].Grid.Sprites, out ArraySegment<Sprite?> second));
        Assert.Same(first.Array, second.Array);
        Assert.NotNull(maps[1].Grid.Shapes[1]);
    }

    private static Scene Load(string document) =>
        Registry.Value.Create(new SceneKey("scenes/level"), SceneDocument.ParseUtf8(Encoding.UTF8.GetBytes(document)));

    // Loads document once to warm, then measures loading a copy of it, parse and composition both.
    private void Report(string label, string document, int count, long budget)
    {
        _ = Load(document);

        byte[] utf8 = Encoding.UTF8.GetBytes(document);
        long before = StepMeasurement.Start();
        _ = Registry.Value.Create(new SceneKey("scenes/level"), SceneDocument.ParseUtf8(utf8));
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine(FormattableString.Invariant($"{label}: {bytes} bytes for {count}, {bytes / count} each, budget {budget / count} each"));
        Assert.True(bytes <= budget, FormattableString.Invariant($"{label} allocated {bytes} bytes for {count}, over its budget of {budget}."));
    }
}
