using System.Collections.Immutable;
using System.Reflection;
using Capsule.Scenes;
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

        // The shell's own source calls CapsuleBoot.Configure. A clean compilation is the entry
        // point standing up over the referenced assembly's registry.
        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));
        Assert.NotNull(updated.GetTypeByMetadataName("Capsule.Generated.CapsuleBoot"));

        // The platform is a required argument of the entry point, never a lever the shell may omit.
        string generated = GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleBootFile);
        Assert.Contains("Configure(string gameName, global::Capsule.Runtime.HostPlatform platform)", generated, StringComparison.Ordinal);
    }

    // The build writes CapsuleStrings into a logic assembly with string tables, and the stand-in here
    // declares what it does. A game with none has no such type to name.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheShell_PassesTheStringTable_OnlyWhenTheGameHasOne(bool strings)
    {
        const string withStrings = """
            using Capsule.Scenes;

            namespace Game
            {
                public sealed class Room01(SceneContent content) : Scene(content);
            }

            namespace Capsule.Generated
            {
                public static class CapsuleStrings
                {
                    public static Capsule.Localization.StringTable Table => null!;
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.CompileShell(ShellSource, strings ? withStrings : LogicSource);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));
        Assert.Equal(
            strings,
            GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleBootFile).Contains("Payloads, global::Capsule.Generated.CapsuleStrings.Table);", StringComparison.Ordinal));
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

    // An assembly built against another Capsule can carry a claim this generator cannot read. The shell names it
    // instead of booting without its registry.
    [Fact]
    public void AShellReferencingMalformedRegistryMetadata_NamesTheAssembly()
    {
        const string logic = """
            using Capsule.Scenes;

            [assembly: Capsule.Generated.CapsuleGeneratedRegistryClaim(99, "rooms/opening", typeof(Rooms.Opening))]

            namespace Rooms;

            public sealed class Opening(SceneContent content) : Scene(content);
            """;

        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileShellWithLogicAssemblies(ShellSource, ("Game.Rooms", logic)).Diagnostics;

        Diagnostic error = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal("CAP014", error.Id);
        Assert.Contains("'Game.Rooms'", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void TheShell_AggregatesEveryReferencedLogicAssembly()
    {
        const string actors = """
            using Capsule.Scenes;

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

    // A test composes through CapsuleScenes.Registry and a run through CapsuleBoot's. Both must build the same scene.
    [Fact]
    public void CapsuleScenesRegistry_ComposesADocumentAsTheShellsRegistryDoes()
    {
        const string logic = $$"""
            using Capsule.Scenes;
            using Capsule.Tiles;

            namespace Game;

            {{GeneratorHarness.GameCamera}}

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
        const string logic = $$"""
            using Capsule.Input;
            using Capsule.Scenes;

            namespace Game;

            public sealed class Room01(SceneContent content) : Scene(content);

            {{GeneratorHarness.Walkthrough}}
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

        // The logic assembly's drivers arrive through its provider. The shell's own is registered
        // in the entry point itself, under its class name.
        string generated = GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleBootFile);
        Assert.Contains("CapsuleRegistryProvider_GameSpecs_", generated, StringComparison.Ordinal);
        GeneratorHarness.AssertPairs(generated, "Idler", "Shell.Idler");
    }
}
