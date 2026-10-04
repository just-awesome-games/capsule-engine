using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// An [Authorable] member typed as an entity class or an interface names another entry of the document by id.
// The scene sets it once every entry is constructed, and the build and the load both check what it names.
public sealed class EntityReferenceTests
{
    private const string Room = "scenes/room.scene.json";

    private const string Game = """
        #nullable enable
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;

        public interface ISwitchable;

        public sealed class Lift(EntitySpawn spawn) : Entity(spawn), ISwitchable;

        public sealed class Shuttle(EntitySpawn spawn) : Entity(spawn);

        // Records what its constructor and its OnStart each see.
        public sealed class FloorSwitch : Entity
        {
            public FloorSwitch(EntitySpawn spawn)
                : base(spawn) => SeenInConstructor = Lift is null && Target is null;

            [Authorable]
            public required Lift Lift { get; set; }

            [Authorable]
            public ISwitchable? Target { get; private set; }

            public bool SeenInConstructor { get; }

            public Lift? SeenOnStart { get; private set; }

            protected override void OnStart() => SeenOnStart = Lift;
        }
        """;

    // The lift at id 3 comes after the switch, and the one at id 1 before it.
    private const string Linked = """
        {"id": 1, "type": "lift", "x": 0, "y": 0},
        {"id": 2, "type": "floor-switch", "x": 0, "y": 0, "lift": 3, "target": 1},
        {"id": 3, "type": "lift", "x": 0, "y": 0}
        """;

    [Fact]
    public void AForwardAndABackwardReference_ResolveAfterEveryEntryIsConstructed()
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = GeneratorHarness.CompileAgainstSources(Game, logic: true, (Room, Document(Linked)));
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Scene scene = Composed(Linked);
        Entity[] entities = scene.Entities.ToArray();
        Entity floorSwitch = entities[1];
        using SimulationHost host = new(scene);

        Assert.True((bool)Member(floorSwitch, "SeenInConstructor")!);
        Assert.Same(entities[2], Member(floorSwitch, "Lift"));
        Assert.Same(entities[0], Member(floorSwitch, "Target"));
        Assert.Same(entities[2], Member(floorSwitch, "SeenOnStart"));
    }

    // A reference is checked when the scene loads.
    [Theory]
    [InlineData(99, "sets 'lift' to 99, which names no entity in the document. Write the id of an entity entry.")]
    [InlineData(5, "sets 'lift' to entity 5, a Shuttle, but the member takes Lift. Write the id of an entity that is a Lift.")]
    public void AReferenceNamingNoEntityItsMemberTakes_FailsTheLoad(int target, string fix)
    {
        string entries = $$$"""
            {"id": 2, "type": "floor-switch", "x": 0, "y": 0, "lift": {{{target}}}},
            {"id": 5, "type": "shuttle", "x": 0, "y": 0}
            """;

        SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(() => Composed(entries));

        Assert.StartsWith("scene document 'scenes/room': entities[0] ('floor-switch') ", failure.Message, StringComparison.Ordinal);
        Assert.Contains(fix, failure.Message, StringComparison.Ordinal);
    }

    // One spelling per kind: a mandatory reference is C#'s required, and Required = true is for every other member.
    [Fact]
    public void RequiredTrueOnAReference_FailsAtTheMember()
    {
        string source = $$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Lift(EntitySpawn spawn) : Entity(spawn);

            public sealed class Door(EntitySpawn spawn) : Entity(spawn)
            {
                [Authorable(Required = true)]
                public Lift Lift { get; set; }
            }
            """;

        Diagnostic refused = Assert.Single(GeneratorHarness.Errors(GeneratorHarness.Compile(source).Diagnostics));
        Assert.Equal("CAP041", refused.Id);
        Assert.EndsWith("Drop Required = true and write C#'s required instead", refused.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static string Document(string entries) =>
        "{\"entities\": [" + entries + "]}";

    // Composes the room through the generated registry.
    private static Scene Composed(string entries)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile(Game);
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Assembly game = GeneratorHarness.Loaded(compiled);
        SceneRegistry registry = (SceneRegistry)game.GetType("Capsule.Generated.CapsuleScenes")!
            .GetProperty("Registry")!.GetValue(null)!;

        return registry.Create(new SceneKey("scenes/room"), SceneDocumentFile.Parse(Document(entries)));
    }

    private static object? Member(Entity entity, string name) => entity.GetType().GetProperty(name)!.GetValue(entity);
}
