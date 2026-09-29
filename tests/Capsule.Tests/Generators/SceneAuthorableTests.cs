using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// A document's own properties set the members its composing scene class marks [Authorable]. They land once every
// entry is constructed and before the derived constructor body runs, and the build checks them like an entry's.
public sealed class SceneAuthorableTests
{
    private const string Hall = "scenes/hall.scene.json";

    private const string Game = """
        #nullable enable
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

            public int SeenFloor { get; }

            public Lift? SeenLift { get; }
        }
        """;

    private const string Authored = """{"floor": 3, "lift": 1, "title": "document"}""";

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

    // The engine's Scene declares no members, so a document no class claims authors none.
    [Theory]
    [InlineData(Hall, """{"lift": 1}""", "CAP040", "the document omits 'floor', which 'Game.Hall' requires. Add \"floor\" to its properties")]
    [InlineData(Hall, """{"floor": 3, "lift": 1, "music": 2}""", "CAP036", "the document sets 'music', which 'Game.Hall' does not declare")]
    [InlineData("scenes/plain.scene.json", """{"music": 2}""", "CAP036", "the document sets 'music', which 'Capsule.Scenes.Scene' does not declare. Its authorable members are: none")]
    public void ADocumentItsClassRefuses_FailsTheBuildAtItsProperties(string path, string properties, string id, string fix)
    {
        string document = Document(properties);
        (ImmutableArray<Diagnostic> diagnostics, _) = GeneratorHarness.CompileAgainstSources(Game, logic: true, (path, document));

        Diagnostic refused = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal(id, refused.Id);
        Assert.Contains(fix, refused.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        FileLinePositionSpan at = refused.Location.GetLineSpan();
        Assert.Equal((path, 0, document.IndexOf(properties, StringComparison.Ordinal)), (at.Path, at.StartLinePosition.Line, at.StartLinePosition.Character));
    }

    private static string Document(string properties) =>
        "{\"formatVersion\": 7, \"properties\": " + properties + ", \"entities\": [{\"id\": 1, \"type\": \"lift\", \"x\": 0, \"y\": 0}], \"nextEntityId\": 2}";

    // Compiled against the document, so the build's check passes it before the scene is composed.
    private static Scene Composed(string properties)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(Game, logic: true, (Hall, Document(properties)));
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Assembly game = GeneratorHarness.Loaded(compiled);
        SceneRegistry registry = (SceneRegistry)game.GetType("Capsule.Generated.CapsuleScenes")!.GetProperty("Registry")!.GetValue(null)!;

        return registry.CreateFromDocument("scenes/hall", SceneDocumentFile.Parse(Document(properties)));
    }

    private static object? Member(Scene scene, string name) => scene.GetType().GetProperty(name)!.GetValue(scene);
}
