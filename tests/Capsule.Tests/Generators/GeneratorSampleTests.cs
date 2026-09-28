using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

/// <summary>
/// Each emitted file's committed sample under <c>src/Capsule.Generators/</c> against what the generator
/// writes for one small game, so the shape of every file reads without running the generator.
/// </summary>
public sealed class GeneratorSampleTests
{
    private const string Logic = """
        #nullable enable
        using Capsule.Assets;
        using Capsule.Input;
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;

        public enum Glow { Dim, Bright }

        public sealed class Door(EntitySpawn spawn) : Entity(spawn);

        public sealed class Lamp(EntitySpawn spawn) : Entity(spawn)
        {
            [Authorable(Required = true)]
            public Glow Glow { get; private set; }

            [Authorable]
            public TextureHandle Icon { get; set; }

            [Authorable]
            private int _charge = 3;
        }

        public sealed class FloorSwitch(EntitySpawn spawn) : Entity(spawn)
        {
            [Authorable]
            public required Lamp Lamp { get; set; }
        }

        public sealed class MainMenu : Scene;

        [SceneDocument("scenes/hall")]
        public sealed class Hall(SceneContent content) : Scene(content);

        public abstract class PlayableScene(SceneContent content) : Scene(content);

        public sealed class GameCamera : Camera;

        public sealed class Walkthrough : IInputDriver
        {
            public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
            {
                snapshot = DeviceSnapshot.Empty;
                return false;
            }
        }
        """;

    private const string Shell = """
        using Capsule.Input;
        using Capsule.Scenes;

        namespace Shell;

        public sealed class Replay : IInputDriver
        {
            public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
            {
                snapshot = DeviceSnapshot.Empty;
                return false;
            }
        }
        """;

    private static readonly (string Path, string? Content)[] Assets =
    [
        ("textures/lamp.png", null),
        ("scenes/hall.scene.json", """
            {"formatVersion": 7, "camera": "game-camera", "entities": [
                {"id": 1, "type": "lamp", "x": 0, "y": 0, "properties": {"glow": "bright", "icon": "textures/lamp.png"}},
                {"id": 2, "type": "floor-switch", "x": 0, "y": 0, "properties": {"lamp": 1}}
            ], "nextEntityId": 3}
            """),
        ("scenes/room.scene.json", """{"formatVersion": 7, "baseScene": "playable-scene", "camera": "game-camera", "entities": [], "nextEntityId": 1}"""),
    ];

    private static readonly Lazy<Compilation> LogicAssembly = new(() => Clean(GeneratorHarness.CompileAgainstSources(Logic, logic: true, Assets)));

    private static readonly Lazy<Compilation> ShellAssembly = new(() => Clean(GeneratorHarness.CompileShell(Shell, Logic)));

    [Theory]
    [InlineData("Entities/CapsuleEntities", false)]
    [InlineData("Entities/CapsuleAuthorable", false)]
    [InlineData("Scenes/CapsuleScenes", false)]
    [InlineData("Drivers/CapsuleInputDrivers", false)]
    [InlineData("Boot/CapsuleRegistryProvider", false)]
    [InlineData("Boot/CapsuleBoot", true)]
    [InlineData("Pipeline/CapsuleGlobalUsings", false)]
    public void EachCommittedSample_IsWhatTheGeneratorWrites(string sample, bool shell)
    {
        string generated = GeneratorHarness.Emitted((shell ? ShellAssembly : LogicAssembly).Value, Path.GetFileName(sample) + ".g.cs");
        string file = sample.Replace('/', Path.DirectorySeparatorChar) + ".sample.g.cs";
        string committed = Path.Combine(GeneratorDirectory(), file);

        // Byte for byte, so a committed CRLF or BOM is stale too. The samples are UTF-8 without a BOM.
        if (File.Exists(committed) && File.ReadAllBytes(committed).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(generated)))
        {
            return;
        }

        string fresh = Path.Combine(AppContext.BaseDirectory, "generator-samples", file);
        Directory.CreateDirectory(Path.GetDirectoryName(fresh)!);
        File.WriteAllText(fresh, generated);
        Assert.Fail(
            $"{sample}.sample.g.cs differs from what the generator writes. The sample is generated, so a hand edit or a generator "
                + $"change alone makes it stale. Copy the fresh output from '{fresh}' over '{committed}' and commit it.");
    }

    private static Compilation Clean((ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) compiled)
    {
        Assert.Empty(GeneratorHarness.Errors(compiled.Diagnostics));
        Assert.Empty(GeneratorHarness.Errors(compiled.Updated.GetDiagnostics()));

        return compiled.Updated;
    }

    // Walks up from the test binaries to the checkout, which holds the solution beside src/.
    private static string GeneratorDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Capsule.slnx")))
            {
                return Path.Combine(directory.FullName, "src", "Capsule.Generators");
            }
        }

        throw new DirectoryNotFoundException($"No directory above '{AppContext.BaseDirectory}' holds Capsule.slnx. Run the tests from inside the engine checkout.");
    }
}
