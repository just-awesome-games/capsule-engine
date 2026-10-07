using System.Collections.Immutable;
using System.Globalization;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tests.Generators;
using Microsoft.CodeAnalysis;
using Xunit.Abstractions;

namespace Capsule.Tests.Allocation;

// A restart composes the held document again. It should not cost what the document already paid for.
[Collection(StageAllocationCollection.Name)]
public sealed class RestartAllocationTests(ITestOutputHelper output)
{
    private const int Wide = 256;
    private const int High = 256;

    // The map shares the document's cells and the collision grid reads the map's, so only the grid's derived
    // state is new: one byte a cell. One more copy of the cells would cost four bytes a cell.
    [Fact]
    public void RecomposingATileMapDocument_AllocatesOneByteACell()
    {
        SceneRegistry registry = Registry();
        SceneDocument document = SceneDocument.Parse(TerrainDocument());
        Compose(registry, document);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Compose(registry, document);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"recompose of a {Wide}x{High} colliding map: {bytes} bytes"));
        Assert.True(
            bytes < Wide * High * 2,
            FormattableString.Invariant($"Recomposing a {Wide}x{High} map allocated {bytes} bytes, more than its collision state accounts for."));
    }

    private static void Compose(SceneRegistry registry, SceneDocument document)
    {
        using SimulationHost host = new(registry.Create(new SceneKey("scenes/terrain"), document));
    }

    // A colliding map whose every other row is solid, with every tile turned some way.
    private static string TerrainDocument()
    {
        int[] tiles = new int[Wide * High];
        int[] transforms = new int[Wide * High];
        for (int index = 0; index < tiles.Length; index++)
        {
            tiles[index] = index / Wide % 2;
            transforms[index] = index % 8;
        }

        return $$"""
            {"entities": [
              {"type": "tile-map", "tileSize": 16, "width": {{Wide}}, "height": {{High}},
                "tileTypes": [{"name": "empty"}, {"name": "solid", "layer": "solid"}],
                "tiles": [{{string.Join(',', tiles)}}], "transforms": [{{string.Join(',', transforms)}}], "collider": true}
            ]}
            """;
    }

    private static SceneRegistry Registry()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            "namespace Game;\n\npublic sealed class Terrain;\n", logic: true);
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        return (SceneRegistry)GeneratorHarness.Loaded(compiled).GetType("Capsule.Generated.CapsuleScenes")!.GetProperty("Registry")!.GetValue(null)!;
    }
}
