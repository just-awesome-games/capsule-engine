using System.Collections.Immutable;
using System.IO.Compression;
using Capsule.Scenes;
using Capsule.Tests.Documents;
using Capsule.Tests.Generators;
using Capsule.Tiles;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Build;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class NativeSceneToolTests
{
    private const string Authored = SceneDocumentFixtures.AuthoredTileMapAndPlayer;

    private const string Shipped = ToolWorkspace.Out + "/assets/scenes/";

    // A gzip header with a timestamp would make every build of an unchanged document new bytes. An
    // editor's "$schema" stays in the authored file.
    [Fact]
    public void ADocument_ShipsCompactAndGzippedAtItsKey_AndEveryBuildWritesTheSameBytes()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/hall.scene.json", Authored.Replace("{ \"entities\"", "{ \"$schema\": \"scene.schema.json\", \"entities\"", StringComparison.Ordinal));
        string path = Shipped + "hall.scene.json.gz";

        workspace.Succeed();
        byte[] shipped = File.ReadAllBytes(path);
        File.Delete(path);
        workspace.Succeed();

        Assert.Equal(shipped, File.ReadAllBytes(path));
        Assert.Equal([0x1f, 0x8b, 0, 0, 0, 0], [shipped[0], shipped[1], .. shipped[4..8]]);
        using StreamReader inflated = new(new GZipStream(new MemoryStream(shipped), CompressionMode.Decompress));
        string emitted = inflated.ReadToEnd();
        SceneDocument derived = SceneDocument.Parse(emitted);
        Assert.Equal(derived.ToJson(), emitted);
        Assert.NotEqual(Authored, emitted);
        Assert.DoesNotContain("$schema", emitted, StringComparison.Ordinal);
        Assert.Equal(2, derived.Entries[0].Members?.GetProperty("width").GetInt32());
        Assert.Equal("player", derived.Entries[1].Type);
    }

    // An imported document ships at the key of the path its importer wrote it to.
    [Fact]
    public void AnImportedDocument_ShipsAtItsPath()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Upper_Halls/Hall.note", Authored);
        workspace.Configure = static build => build.AddImporter(new NoteImporter());

        workspace.Succeed();

        Assert.Equal("player", Load(ToolWorkspace.Out + "/assets/upper-halls/hall.scene.json.gz").Entries[1].Type);
    }

    [Fact]
    public void AMalformedDocument_FailsByNameAndTheOthersStillDerive()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/hall.scene.json", Authored);
        workspace.Write("Assets/Scenes/broken.scene.json", """{ "entities": [ { "id": 1, "x": 0, "y": 0 } ] }""");

        string errors = workspace.Fail();

        Assert.Contains("Assets/Scenes/broken.scene.json", errors, StringComparison.Ordinal);
        Assert.Contains("has no type", errors, StringComparison.Ordinal);
        Assert.False(File.Exists(Shipped + "broken.scene.json.gz"));
        Assert.True(File.Exists(Shipped + "hall.scene.json.gz"));
    }

    // Every shipped document reaches the generator with its baseScene, and its key reaches the game as a SceneKey.
    [Fact]
    public void EveryDocument_IsHandedToTheGeneratorAndNamedInCode()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/Dev/Room_Wide.scene.json", """{"baseScene": "playable-room", "entities": []}""");
        workspace.Write("Assets/Scenes/room.scene.json", Authored);

        workspace.Succeed();

        string generated = workspace.Generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Matches(
            """\[CapsuleGeneratedSceneDocument\(Key = "scenes/dev/room-wide", BaseScene = "playable-room"\)\]\n +public static global::Capsule\.Scenes\.SceneKey RoomWideScene => new global::Capsule\.Scenes\.SceneKey\("scenes/dev/room-wide"\);""",
            generated);
        Assert.Matches(
            """\[CapsuleGeneratedSceneDocument\(Key = "scenes/room"\)\]\n +public static global::Capsule\.Scenes\.SceneKey RoomScene => new global::Capsule\.Scenes\.SceneKey\("scenes/room"\);""",
            generated);
    }

    // A key that cannot be a member where it lands is refused by the build, never left to fail as a
    // C# error in generated code.
    [Theory]
    [InlineData("already declared", "Assets/halls.scene.json", "Assets/halls-scene/hall.scene.json")]
    [InlineData("inside a generated class of that name", "Assets/room-scene/room.scene.json")]
    public void ADocumentKeyNoMemberCanTake_FailsTheBuildNamingTheDocument(string because, params string[] documents)
    {
        using ToolWorkspace workspace = new();
        foreach (string document in documents)
        {
            workspace.Write(document, """{"entities": []}""");
        }

        string errors = workspace.Fail();

        Assert.Contains(because, errors, StringComparison.Ordinal);
        Assert.Contains(documents[^1], errors, StringComparison.Ordinal);
    }

    // Only the compiled logic assembly knows that a key names a TileMap class. The run after its compile reads the keys
    // its generator recorded and ships that entry's cells compiled, which every run until the next compile reuses. Once
    // every document ships with them, an assembly recording the same keys builds nothing. An entry of any other type
    // ships the members it authors.
    [Fact]
    public void ATileMapClassUnderItsOwnKey_ShipsCompiledFromTheRunAfterTheCompile()
    {
        const string Game = """
            using Capsule.Scenes;
            using Capsule.Tiles;

            namespace Game;

            public sealed class FrozenMap(EntitySpawn spawn) : TileMap(spawn);

            public sealed class Board(EntitySpawn spawn) : Entity(spawn)
            {
                [Authorable]
                public System.ReadOnlyMemory<int> Tiles { get; private set; }

                [Authorable]
                public int Width { get; private set; }

                [Authorable]
                public int Height { get; private set; }
            }
            """;
        int[] tiles = new int[40];
        int[] transforms = new int[40];
        (tiles[3], tiles[37], transforms[37]) = (1, 1, (int)TileTransform.FlipX);
        string document = $$"""
            {"entities": [
                {"type": "frozen-map", "tileSize": 16, "width": 20, "height": 2, "tileTypes": [{"name": "empty"}, {"name": "ice"}],
                 "tiles": [{{string.Join(", ", tiles)}}], "transforms": [{{string.Join(", ", transforms)}}]},
                {"type": "board", "width": 2, "height": 1, "tiles": [3, 4]}
            ]}
            """;
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(Game, logic: true, ("scenes/hall.scene.json", document));
        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/hall.scene.json", document);
        string assembly = workspace.Write("obj/Game.dll", []);
        Assert.True(compiled.Emit(assembly).Success);
        string path = Shipped + "hall.scene.json.gz";

        workspace.Succeed();
        Assert.True(Load(path).Entries[0].Authored!.TryGetValue("tiles", out _));

        // A run with any defect, even one a step before the scenes reports, keeps the old keys, and its retry builds again.
        workspace.Write("Assets/broken.sheet.json", "{");
        workspace.Fail("--tile-maps", assembly);
        File.Delete("Assets/broken.sheet.json");
        workspace.Succeed("--tile-maps", assembly);
        Assert.NotEmpty(workspace.Output);
        SceneDocument shipped = Load(path);
        Assert.Equal((false, true), (shipped.Entries[0].Authored!.TryGetValue("tiles", out _), shipped.Entries[0].Authored!.TryGetValue("blocks", out _)));
        Assert.True(shipped.Entries[1].Authored!.TryGetValue("tiles", out _));
        SceneRegistry registry = (SceneRegistry)GeneratorHarness.Loaded(compiled).GetType("Capsule.Generated.CapsuleScenes")!.GetProperty("Registry")!.GetValue(null)!;
        TileMap map = Assert.IsAssignableFrom<TileMap>(registry.Create(new SceneKey("scenes/hall"), shipped).Entities[0]);
        for (int cell = 0; cell < tiles.Length; cell++)
        {
            Assert.Equal((tiles[cell] == 1 ? "ice" : TileGrid.EmptyTileName, (TileTransform)transforms[cell]), (map.TileAt(cell % 20, cell / 20).Name, map.TransformAt(cell % 20, cell / 20)));
        }

        workspace.Succeed("--tile-maps", assembly);
        Assert.Empty(workspace.Output);
        workspace.Succeed();
        Assert.Equal((false, []), (Load(path).Entries[0].Authored!.TryGetValue("tiles", out _), workspace.Built));
    }

    private static SceneDocument Load(string path)
    {
        using FileStream shipped = File.OpenRead(path);

        return ShippedSceneDocument.Read(shipped);
    }
}
