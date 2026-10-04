using System.Collections.Immutable;
using System.Reflection;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// An [Authorable] member whose class declares [Authorable] members is a JSON object of them, at any depth. The
// document fills the instance the member holds, or constructs the class or the subclass its type key names.
public sealed class AuthorableObjectTests
{
    private const string Game = """
        #nullable enable
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;

        public sealed class Lamp(EntitySpawn spawn) : Entity(spawn);

        public sealed class Trigger : Component
        {
            [Authorable(Required = true)]
            public Lamp? Target { get; set; }

            [Authorable]
            public float Delay { get; set; }
        }

        public abstract class Movement
        {
            [Authorable]
            public float Speed { get; set; }
        }

        public sealed class Patrol : Movement
        {
            [Authorable]
            public float Reach { get; set; }
        }

        public sealed class Switch : Entity
        {
            [Authorable]
            private readonly Trigger _trigger = new();

            public Switch(EntitySpawn spawn)
                : base(spawn)
            {
                Add(_trigger);
                DelayInConstructor = _trigger.Delay;
            }

            [Authorable]
            public Movement? Movement { get; set; }

            public float DelayInConstructor { get; }

            public Trigger Trigger => _trigger;
        }

        internal sealed class GameCamera : Camera;
        """;

    private static readonly Lazy<SceneRegistry> Registry = new(static () =>
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile(Game);
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        return (SceneRegistry)GeneratorHarness.Loaded(compiled).GetType("Capsule.Generated.CapsuleScenes")!
            .GetProperty("Registry")!.GetValue(null)!;
    });

    // The object's values land before the constructor body runs, as the entity's own do.
    [Fact]
    public void AConstructorBody_ReadsTheValuesItsComponentWasAuthored()
    {
        Entity placed = Composed("""{"entities": [{"id": 1, "type": "lamp"}, {"type": "switch", "trigger": {"target": 1, "delay": 0.5}}]}""").Entities[1];

        Assert.Equal(0.5f, Member(placed, "DelayInConstructor"));
    }

    [Fact]
    public void AType_ConstructsTheSubclassItNames()
    {
        Scene scene = Composed("""
            {
              "camera": {"type": "game-camera"},
              "entities": [{"id": 1, "type": "lamp"}, {"type": "switch", "trigger": {"target": 1}, "movement": {"type": "patrol", "speed": 30, "reach": 64}}]
            }
            """);
        object movement = Member(scene.Entities[1], "Movement")!;

        Assert.Equal("Game.GameCamera", scene.Camera.GetType().FullName);
        Assert.Equal("Game.Patrol", movement.GetType().FullName);
        Assert.Equal((30f, 64f), ((float)Member(movement, "Speed")!, (float)Member(movement, "Reach")!));
    }

    // A reference inside an object is set once every entry is built, as one on the entity is.
    [Fact]
    public void AReferenceInsideAnObject_NamesAnotherEntry()
    {
        Scene scene = Composed("""{"entities": [{"type": "switch", "trigger": {"target": 2}}, {"id": 2, "type": "lamp"}]}""");

        Assert.Same(scene.Entities[1], Member(Member(scene.Entities[0], "Trigger")!, "Target"));
    }

    [Fact]
    public void AKeyNoNestedMemberTakes_FailsTheLoadNamingItsPath()
    {
        SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(
            () => Composed("""{"entities": [{"id": 1, "type": "lamp"}, {"type": "switch", "trigger": {"target": 1, "dely": 2}}]}"""));

        Assert.Contains("entities[1] ('switch') sets 'trigger.dely', which no authorable member takes. 'trigger's authorable members are: delay, target.", failure.Message, StringComparison.Ordinal);
    }

    // An abstract member's class is never constructed, so an object without a type has nothing to fill.
    [Fact]
    public void AnAbstractMemberWithNoType_FailsTheLoadNamingTheTypes()
    {
        SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(
            () => Composed("""{"entities": [{"id": 1, "type": "lamp"}, {"type": "switch", "trigger": {"target": 1}, "movement": {"speed": 30}}]}"""));

        Assert.Contains("sets 'movement' with no type, and the member holds no object to fill. Write one of: patrol.", failure.Message, StringComparison.Ordinal);
    }

    private static Scene Composed(string json) => Registry.Value.Create(new SceneKey("scenes/room"), SceneDocument.Parse(json));

    private static object? Member(object owner, string name) =>
        owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(owner);
}
