using System.Collections.Immutable;
using System.Reflection;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tiles;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class CapsuleBootGeneratorTests
{
    private const string LogicSource = """
        using Capsule.Scenes;

        namespace Game;

        public sealed class Room01(SceneContent content) : Scene(content);
        """;

    private const string ShellSource = """
        namespace Shell;

        public static class Program
        {
            public static void Boot() => CapsuleBoot.Configure("Spec Game", new SpecPlatform()).WithWindow(320, 180);
        }

        public sealed class SpecPlatform : Capsule.Runtime.HostPlatform
        {
            protected override System.IO.Stream OpenContent(string relativePath) => throw new System.NotSupportedException();

            protected override Capsule.Persistence.ISaveStorage OpenSaveStorage(string localFolderName) => throw new System.NotSupportedException();
        }
        """;

    [Fact]
    public void TheShell_BootsThroughTheRegistryOfTheGameAssemblyItReferences()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.CompileShell(ShellSource, LogicSource);

        // The shell's own source calls CapsuleBoot.Configure, so a clean compilation is the entry
        // point standing up over the referenced assembly's registry.
        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));
        Assert.NotNull(updated.GetTypeByMetadataName("Capsule.Generated.CapsuleBoot"));

        // The platform is a required argument of the entry point, never a lever the shell may omit.
        string generated = GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleBootFile);
        Assert.Contains("Configure(string gameName, global::Capsule.Runtime.HostPlatform platform)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShell_GetsNoRegistryOfItsOwn_ThoughItSeesCapsuleScenesThroughTheGame()
    {
        Compilation updated = GeneratorHarness.CompileShell(ShellSource, LogicSource).Updated;

        Assert.NotNull(updated.GetTypeByMetadataName("Capsule.Scenes.Scene"));
        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.CapsuleScenesFile));
        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.CapsuleEntitiesFile));
    }

    [Fact]
    public void TheGameAssembly_GetsNoEntryPoint_ThoughItSeesTheEngineHost()
    {
        Compilation updated = GeneratorHarness.Compile(LogicSource).Updated;

        Assert.NotNull(updated.GetTypeByMetadataName("Capsule.Runtime.CapsuleEngine"));
        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.CapsuleBootFile));
    }

    [Fact]
    public void AShellReferencingNoLogicAssembly_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileShell(ShellSource).Diagnostics;

        Assert.Equal("CAP015", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void TheShell_AggregatesEveryReferencedLogicAssembly()
    {
        const string actors = """
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;

            namespace Actors;

            internal sealed class Player(EntitySpawn spawn) : Entity(spawn);
            """;
        const string rooms = """
            using Capsule.Scenes;

            namespace Rooms;

            internal sealed class Opening(SceneContent content) : Scene(content);
            """;

        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) =
            GeneratorHarness.CompileShellWithLogicAssemblies(
                ShellSource,
                ("Game.Actors", actors),
                ("Game.Rooms", rooms));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));

        // Each referenced logic assembly hands its registry over through a provider of its own.
        string generated = GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleBootFile);
        Assert.Contains("CapsuleRegistryProvider_Game_Actors_", generated, StringComparison.Ordinal);
        Assert.Contains("CapsuleRegistryProvider_Game_Rooms_", generated, StringComparison.Ordinal);
    }

    // A test composes through CapsuleScenes.Registry and a run through CapsuleBoot's, so both must build the same scene.
    [Fact]
    public void CapsuleScenesRegistry_ComposesADocumentAsTheShellsRegistryDoes()
    {
        const string logic = """
            using Capsule.Scenes;
            using Capsule.Tiles;

            namespace Game;

            public sealed class GameCamera : Camera;

            public sealed class Brick : TileType;

            [TypeKey("scenes/wall")]
            public sealed class Wall(SceneContent content) : Scene(content)
            {
                [Authorable]
                public int Floor { get; private set; }
            }
            """;
        const string document = """
            {"camera": {"type": "game-camera"}, "floor": 2, "entities": [
              {"id": 1, "type": "tile-map", "tileSize": 16, "width": 1, "height": 1,
                "tileTypes": [{"name": "empty"}, {"name": "wall", "layer": "solid", "type": "brick"}], "tiles": [1]}
            ]}
            """;

        using GeneratorHarness.ShellContext loaded = GeneratorHarness.LoadedShell(ShellSource, logic, ("scenes/wall.scene.json", document));
        SceneRegistry boot = (SceneRegistry)loaded.Shell.GetType("Capsule.Generated.CapsuleBoot")!
            .GetProperty("Scenes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        SceneRegistry scenes = (SceneRegistry)loaded.LoadFromAssemblyName(new AssemblyName("GameSpecs")).GetType("Capsule.Generated.CapsuleScenes")!
            .GetProperty("Registry")!.GetValue(null)!;

        (string?, string?, object?, string?) Composed(SceneRegistry registry)
        {
            Scene scene = registry.Create(new SceneKey("scenes/wall"), SceneDocument.Parse(document));
            TileMap map = Assert.IsType<TileMap>(Assert.Single(scene.Entities.ToArray()));

            return (scene.GetType().FullName, scene.Camera.GetType().FullName, scene.GetType().GetProperty("Floor")!.GetValue(scene), map.TileAt(0, 0).GetType().FullName);
        }

        Assert.Equal(("Game.Wall", "Game.GameCamera", (object?)2, "Game.Brick"), Composed(boot));
        Assert.Equal(Composed(boot), Composed(scenes));
    }

    [Fact]
    public void TheShell_TakesDriversFromItsLogicAssembliesAndFromItsOwnCode()
    {
        const string logic = """
            using Capsule.Input;
            using Capsule.Scenes;

            namespace Game;

            public sealed class Room01(SceneContent content) : Scene(content);

            public sealed class Walkthrough : IInputDriver
            {
                public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
                {
                    snapshot = DeviceSnapshot.Empty;
                    return tick < 60;
                }
            }
            """;
        const string shell = """
            using Capsule.Input;
            using Capsule.Scenes;

            namespace Shell;

            public sealed class Idler : IInputDriver
            {
                public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
                {
                    snapshot = DeviceSnapshot.Empty;
                    return tick < 1;
                }
            }

            public static class Program
            {
                public static void Boot() => CapsuleBoot.Configure("Spec Game", null!).WithWindow(320, 180);
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.CompileShell(shell, logic);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));

        // The logic assembly's drivers arrive through its provider; the shell's own is registered
        // in the entry point itself, under its class name.
        string generated = GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleBootFile);
        Assert.Contains("CapsuleRegistryProvider_GameSpecs_", generated, StringComparison.Ordinal);
        GeneratorHarness.AssertPairs(generated, "Idler", "Shell.Idler");
    }

    // Each logic assembly refuses its own duplicates. Only the shell sees two assemblies claim one key.
    [Theory]
    [InlineData(
        "namespace First; public sealed class Chest(Capsule.Scenes.Spawning.EntitySpawn spawn) : Capsule.Scenes.Entity(spawn);",
        "namespace Second; [Capsule.Scenes.TypeKey(\"chest\")] public sealed class IronChest(Capsule.Scenes.Spawning.EntitySpawn spawn) : Capsule.Scenes.Entity(spawn);",
        "CAP003")]
    [InlineData(
        "namespace First; [Capsule.Scenes.TypeKey(\"opening\")] public sealed class FirstOpening(Capsule.Scenes.SceneContent content) : Capsule.Scenes.Scene(content);",
        "namespace Second; [Capsule.Scenes.TypeKey(\"opening\")] public sealed class SecondOpening(Capsule.Scenes.SceneContent content) : Capsule.Scenes.Scene(content);",
        "CAP005")]
    public void OneKeyClaimedByTwoLogicAssemblies_FailsTheShellBuild(string first, string second, string id)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileShellWithLogicAssemblies(
            ShellSource,
            ("Game.First", first),
            ("Game.Second", second)).Diagnostics;

        Assert.Equal(id, Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }
}
