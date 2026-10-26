using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

/// <summary>What each registry the generator emits claims, passes over or refuses, rule by rule across every kind it registers.</summary>
public sealed class RegistryClaimTests
{
    private const string Preamble = """
        using System.Numerics;
        using Capsule.Input;
        using Capsule.Scenes;

        namespace Game;
        """;

    // A shape the registry can never use is passed over in silence or registered under no name. One it
    // could use but for a defect the author can fix fails the build with that defect's diagnostic.
    [Theory]
    [InlineData("entity", "public sealed class Player : Entity { public Player() : base(Vector2.Zero) { } }", null)]
    [InlineData("entity", "public sealed class Player : Entity { public Player(Vector2 at, int hp) : base(at) { } }", null)]
    [InlineData("entity", "public abstract class Player : Entity { protected Player(EntitySpawn spawn) : base(spawn) { } }", null)]
    [InlineData("entity", "public sealed class Player { public Player(EntitySpawn spawn) { } }", null)]
    [InlineData("entity", "[TypeKey(\"player\")] public abstract class Player : Entity { protected Player(EntitySpawn spawn) : base(spawn) { } }", "CAP001")]
    [InlineData("entity", "[TypeKey(\"player\")] public sealed class Player : Entity { public Player() : base(Vector2.Zero) { } }", "CAP002")]
    [InlineData("entity", "[TypeKey(\"  \")] public sealed class Player(EntitySpawn spawn) : Entity(spawn);", "CAP004")]
    [InlineData("entity", "public static class Entities { private sealed class Player(EntitySpawn spawn) : Entity(spawn); }", "CAP008")]
    [InlineData("entity", "public sealed class Player : Entity { public Player(EntitySpawn spawn) : base(spawn) { } public Player(in EntitySpawn spawn) : base(spawn) { } }", "CAP010")]
    [InlineData("scene", "public sealed class Room : Scene { public Room(int depth) { } }", null)]
    [InlineData("scene", "public sealed class Room : Scene { private Room() { } }", null)]
    [InlineData("scene", "public abstract class Room : Scene { protected Room(SceneContent content) : base(content) { } }", null)]
    [InlineData("scene", "public sealed class Room { public Room() { } }", null)]
    [InlineData("scene", "[TypeKey(\"menu\")] public sealed class Room : Scene;", "CAP007")]
    [InlineData("scene", "public static class Scenes { private sealed class Room(SceneContent content) : Scene(content); }", "CAP008")]
    [InlineData("scene", "public sealed class Room : Scene { public Room() { } public Room(SceneContent content) : base(content) { } }", "CAP009")]
    [InlineData("payload", "public record Arrival(string Entrance) : IScenePayload;", null)]
    [InlineData("payload", "public abstract class Arrival : IScenePayload;", null)]
    [InlineData("payload", "public sealed class Arrival<T> : IScenePayload;", null)]
    [InlineData("driver", "public sealed class Seeded(int seed) : IInputDriver { public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot) { snapshot = DeviceSnapshot.Empty; return tick < seed; } }", null)]
    public void AShapeTheRegistryCannotUse_IsPassedOverOrFailsTheBuild(string kind, string declaration, string? id)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.Compile($$"""
            {{Preamble}}

            {{declaration}}
            """);

        if (id is not null)
        {
            Assert.Equal(id, Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
            return;
        }

        (string file, string type) = kind switch
        {
            "entity" => (GeneratorHarness.CapsuleEntitiesFile, "Game.Player"),
            "scene" => (GeneratorHarness.CapsuleScenesFile, "Game.Room"),
            "payload" => (GeneratorHarness.CapsuleScenePayloadsFile, "Arrival"),
            _ => (GeneratorHarness.CapsuleInputDriversFile, "Seeded"),
        };

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(type, GeneratorHarness.Emitted(updated, file), StringComparison.Ordinal);
    }

    // Two declarations claiming one key fail the build with one error naming both. A logic assembly
    // refuses its own pairs. Only the shell sees two logic assemblies claim one key.
    [Theory]
    [InlineData("entity")]
    [InlineData("entity across domain segments")]
    [InlineData("camera across domain segments")]
    [InlineData("scene")]
    [InlineData("base scene")]
    [InlineData("driver")]
    [InlineData("payload")]
    [InlineData("entity across logic assemblies")]
    [InlineData("scene across logic assemblies")]
    public void TwoDeclarationsClaimingOneKey_FailTheBuildNamingBoth(string kind)
    {
        (ImmutableArray<Diagnostic> diagnostics, string id, string key, string first, string second) = kind switch
        {
            "entity" => (Pair(
                "Game", "public sealed class Chest(EntitySpawn spawn) : Entity(spawn);",
                "Game", "[TypeKey(\"chest\")] public sealed class IronChest(EntitySpawn spawn) : Entity(spawn);"),
                "CAP003", "chest", "Game.Chest", "Game.IronChest"),
            "entity across domain segments" => (Rooted(
                "Game.Cameras", "public sealed class Bat(EntitySpawn spawn) : Entity(spawn);",
                "Game.Entities", "public sealed class Bat(EntitySpawn spawn) : Entity(spawn);"),
                "CAP003", "bat", "Game.Cameras.Bat", "Game.Entities.Bat"),
            "camera across domain segments" => (Rooted(
                "Game.Cameras", "public sealed class Bat : Camera;",
                "Game.Entities", "public sealed class Bat : Camera;"),
                "CAP003", "bat", "Game.Cameras.Bat", "Game.Entities.Bat"),
            "scene" => (Pair(
                "Game", "public sealed class Room01(SceneContent content) : Scene(content);",
                "Game.Deep", "public sealed class Room01(SceneContent content) : Scene(content);"),
                "CAP005", "room-01", "Game.Room01", "Game.Deep.Room01"),

            // A baseScene resolves against every abstract scene by the type key rule.
            "base scene" => (Pair(
                "Game", "public abstract class PlayableRoom(SceneContent content) : Scene(content);",
                "Game.Deep", "public abstract class PlayableRoom(SceneContent content) : Scene(content);"),
                "CAP003", "playable-room", "Game.PlayableRoom", "Game.Deep.PlayableRoom"),
            "driver" => (Pair("Game.Early", GeneratorHarness.Walkthrough, "Game.Late", GeneratorHarness.Walkthrough),
                "CAP020", "Walkthrough", "Game.Early.Walkthrough", "Game.Late.Walkthrough"),
            "payload" => (Pair(
                "Game.Early", "public sealed class Checkpoint : IScenePayload;",
                "Game.Late", "public sealed class Checkpoint : IScenePayload;"),
                "CAP042", "Checkpoint", "Game.Early.Checkpoint", "Game.Late.Checkpoint"),
            "entity across logic assemblies" => (Shells(
                "namespace First; public sealed class Chest(Capsule.Scenes.EntitySpawn spawn) : Capsule.Scenes.Entity(spawn);",
                "namespace Second; [Capsule.Scenes.TypeKey(\"chest\")] public sealed class IronChest(Capsule.Scenes.EntitySpawn spawn) : Capsule.Scenes.Entity(spawn);"),
                "CAP003", "chest", "First.Chest", "Second.IronChest"),
            _ => (Shells(
                "namespace First; [Capsule.Scenes.TypeKey(\"opening\")] public sealed class FirstOpening(Capsule.Scenes.SceneContent content) : Capsule.Scenes.Scene(content);",
                "namespace Second; [Capsule.Scenes.TypeKey(\"opening\")] public sealed class SecondOpening(Capsule.Scenes.SceneContent content) : Capsule.Scenes.Scene(content);"),
                "CAP005", "opening", "First.FirstOpening", "Second.SecondOpening"),
        };

        Diagnostic collision = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal(id, collision.Id);

        string message = collision.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(first, message, StringComparison.Ordinal);
        Assert.Contains(second, message, StringComparison.Ordinal);
        Assert.Contains($"'{key}'", message, StringComparison.Ordinal);
    }

    private static ImmutableArray<Diagnostic> Pair(string firstSpace, string first, string secondSpace, string second) =>
        GeneratorHarness.Compile(Spaces(firstSpace, first, secondSpace, second)).Diagnostics;

    // In an assembly rooted at Game, where every kind drops any domain segment from its key.
    private static ImmutableArray<Diagnostic> Rooted(string firstSpace, string first, string secondSpace, string second) =>
        GeneratorHarness.CompileIn("Game", Spaces(firstSpace, first, secondSpace, second)).Diagnostics;

    private static ImmutableArray<Diagnostic> Shells(string first, string second) =>
        GeneratorHarness.CompileShellWithLogicAssemblies(
            "namespace Shell; public static class Program { public static void Boot() => CapsuleBoot.Configure(\"Spec Game\"); }",
            ("Game.First", first),
            ("Game.Second", second)).Diagnostics;

    private static string Spaces(string firstSpace, string first, string secondSpace, string second) => $$"""
        using Capsule.Input;
        using Capsule.Scenes;

        namespace {{firstSpace}}
        {
        {{first}}
        }

        namespace {{secondSpace}}
        {
        {{second}}
        }
        """;
}
