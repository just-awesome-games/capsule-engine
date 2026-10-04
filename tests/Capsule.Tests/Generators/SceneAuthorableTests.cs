using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// A document's own members set those its composing scene class marks [Authorable]. They land once every entry is
// constructed and before the derived constructor body runs, and the load checks them like an entry's.
public sealed class SceneAuthorableTests
{
    private const string Hall = "scenes/hall.scene.json";

    private const string Game = """
        #nullable enable
        using Capsule.Rendering;
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;

        public sealed class Lift(EntitySpawn spawn) : Entity(spawn);

        public abstract class Level(SceneContent content) : Scene(content)
        {
            [Authorable(Required = true)]
            public int Floor { get; private set; }
        }

        // Records what its constructor body sees, then assigns a member of its own.
        [SceneDocument("scenes/hall")]
        public sealed class Hall : Level
        {
            public Hall(SceneContent content)
                : base(content)
            {
                SeenFloor = Floor;
                SeenLift = Lift;
                Title = "code";
            }

            [Authorable]
            public required Lift Lift { get; set; }

            [Authorable]
            public string Title { get; set; } = "none";

            [Authorable]
            public Rect Bounds { get; private set; }

            public int SeenFloor { get; }

            public Lift? SeenLift { get; }
        }
        """;

    // Mirrors a game's test assembly: an abstract room no document names, and a subclass with no registration of its own.
    private const string Rooms = """
        #nullable enable
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;

        public sealed class Lift(EntitySpawn spawn) : Entity(spawn);

        public abstract class Room(SceneContent content) : Scene(content)
        {
            [Authorable]
            public Lift? Start { get; private set; }
        }

        public abstract class Stage(SceneContent content) : Room(content)
        {
            [Authorable]
            public int Floor { get; private set; }
        }

        public abstract class Quiet(SceneContent content) : Scene(content);

        public sealed class TestStage : Stage
        {
            internal TestStage(SceneContent content)
                : base(content)
            {
            }
        }
        """;

    private const string Authored = """{"floor": 3, "lift": 1, "title": "document", "bounds": [0, -16, 320, 240]}""";

    [Fact]
    public void TheConstructorBody_SeesTheDocumentsValues_AndItsOwnAssignmentWins()
    {
        Scene hall = Composed(Authored);

        Assert.Equal(3, Member(hall, "SeenFloor"));
        Assert.Equal("code", Member(hall, "Title"));
    }

    [Fact]
    public void AReferenceMember_IsReadableInTheConstructorBody()
    {
        Scene hall = Composed(Authored);

        Assert.Same(Assert.Single(hall.Entities.ToArray()), Member(hall, "SeenLift"));
    }

    [Fact]
    public void ARectMember_IsSetFromItsFourEdges()
    {
        Assert.Equal(new Rect(0f, -16f, 320f, 240f), Member(Composed(Authored), "Bounds"));
    }

    // The engine's Scene declares no members, so a document no class claims authors none.
    [Theory]
    [InlineData(Hall, """{"lift": 1}""", "the scene document omits 'floor', which its class requires. Add \"floor\" to the document.")]
    [InlineData(Hall, """{"floor": 3, "lift": 1, "music": 2}""", "the scene document sets 'music', which no authorable member takes. Its authorable members are: camera, size, clearColor, ambient, sampling, floor, title, bounds, lift.")]
    [InlineData("scenes/plain.scene.json", """{"music": 2}""", "the scene document sets 'music', which no authorable member takes. Its authorable members are: camera, size, clearColor, ambient, sampling.")]
    public void ADocumentItsClassRefuses_FailsTheLoad(string path, string members, string fix)
    {
        SceneDocumentFormatException refused = Assert.Throws<SceneDocumentFormatException>(() => Composed(members, path));

        Assert.Contains(fix, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestSubclassOfAnAbstractScene_GetsTheDocumentsMembers_ThroughContent()
    {
        Assembly game = Loaded(Rooms);
        SceneContent content = Content(game, "Game.Stage", """{"start": 1, "floor": 3}""");

        Scene stage = (Scene)Activator.CreateInstance(game.GetType("Game.TestStage")!, BindingFlags.Instance | BindingFlags.NonPublic, null, [content], null)!;

        Assert.Same(Assert.Single(stage.Entities.ToArray()), Member(stage, "Start"));
        Assert.Equal(3, Member(stage, "Floor"));
    }

    private static Assembly Loaded(string source)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(source, logic: true);
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        return GeneratorHarness.Loaded(compiled);
    }

    private static SceneRegistry Registry(Assembly game) =>
        (SceneRegistry)game.GetType("Capsule.Generated.CapsuleScenes")!.GetProperty("Registry")!.GetValue(null)!;

    private static SceneContent Content(Assembly game, string sceneType, string members) =>
        (SceneContent)typeof(SceneRegistry).GetMethod(nameof(SceneRegistry.Content))!
            .MakeGenericMethod(game.GetType(sceneType)!)
            .Invoke(Registry(game), BindingFlags.DoNotWrapExceptions, null, [SceneDocument.Parse(Document(members))], null)!;

    // The document's members, written as one object, beside its one entry.
    private static string Document(string members) =>
        "{" + members.Trim()[1..^1] + ", \"entities\": [{\"id\": 1, \"type\": \"lift\", \"x\": 0, \"y\": 0}]}";

    private static Scene Composed(string members, string path = Hall)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(Game, logic: true, (path, Document(members)));
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        return Registry(GeneratorHarness.Loaded(compiled)).Create(new SceneKey(path[..^".scene.json".Length]), SceneDocument.Parse(Document(members)));
    }

    private static object? Member(Scene scene, string name) => scene.GetType().GetProperty(name)!.GetValue(scene);
}
