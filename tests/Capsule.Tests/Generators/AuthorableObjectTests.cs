using System.Collections.Immutable;
using System.Reflection;
using Capsule.Scenes;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// An [Authorable] member whose class declares [Authorable] members is a JSON object of them, at any depth. The
// document fills the instance the member holds, or constructs the class or the subclass its type key names.
public sealed class AuthorableObjectTests
{
    private const string Game = """
        #nullable enable
        using Capsule.Scenes;

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

            // A set-only member holds no instance for an object read to fill.
            [Authorable]
            public Movement? Next { set => NextSet = value; }

            public Movement? NextSet { get; private set; }

            public float DelayInConstructor { get; }

            public Trigger Trigger => _trigger;
        }

        internal sealed class GameCamera : Camera;
        """;

    private static readonly Lazy<SceneRegistry> Registry = new(static () => GeneratorHarness.Registry(GeneratorHarness.Compile(Game)));

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

    [Fact]
    public void ASetOnlyMember_TakesTheObjectItsTypeNames()
    {
        Entity placed = Composed("""{"entities": [{"id": 1, "type": "lamp"}, {"type": "switch", "trigger": {"target": 1}, "next": {"type": "patrol", "reach": 8}}]}""").Entities[1];

        Assert.Equal(8f, (float)Member(Member(placed, "NextSet")!, "Reach")!);
    }

    // A reference inside an object is set once every entry is built, as one on the entity is.
    [Fact]
    public void AReferenceInsideAnObject_NamesAnotherEntry()
    {
        Scene scene = Composed("""{"entities": [{"type": "switch", "trigger": {"target": 2}}, {"id": 2, "type": "lamp"}]}""");

        Assert.Same(scene.Entities[1], Member(Member(scene.Entities[0], "Trigger")!, "Target"));
    }

    [Fact]
    public void AKeyNoNestedMemberTakes_FailsTheLoadNamingItsPath() =>
        GeneratorHarness.AssertLoadRefused(
            () => Composed("""{"entities": [{"id": 1, "type": "lamp"}, {"type": "switch", "trigger": {"target": 1, "dely": 2}}]}"""),
            "scene document 'scenes/room': entities[1] ('switch') ",
            "sets 'trigger.dely', which no authorable member takes. 'trigger's authorable members are: delay, target.");

    // An abstract member's class is never constructed. An object without a type has nothing to fill.
    [Fact]
    public void AnAbstractMemberWithNoType_FailsTheLoadNamingTheTypes() =>
        GeneratorHarness.AssertLoadRefused(
            () => Composed("""{"entities": [{"id": 1, "type": "lamp"}, {"type": "switch", "trigger": {"target": 1}, "movement": {"speed": 30}}]}"""),
            "scene document 'scenes/room': entities[1] ('switch') ",
            "sets 'movement' with no type, and the member holds no object to fill. Write one of: patrol.");

    // A subclass's Fill sets what its base's Fill sets by calling it. Generated code then does not grow with inherited
    // members. A subclass hiding an inherited member reads every member itself, since its base's Fill would read the
    // key as the hidden member's type.
    [Fact]
    public void ASubclassFill_SetsInheritedMembersThroughItsBase_UnlessItHidesOne()
    {
        const string Source = """
            #nullable enable
            using Capsule.Scenes;

            namespace Game;

            public abstract class Movement
            {
                [Authorable]
                public string Speed { get; set; } = "";
            }

            public sealed class Patrol : Movement
            {
                [Authorable]
                public float Reach { get; set; }
            }

            public sealed class Dash : Movement
            {
                [Authorable]
                public new float Speed { get; set; }
            }

            public sealed class Mover(EntitySpawn spawn) : Entity(spawn)
            {
                [Authorable]
                public Movement? Movement { get; set; }
            }
            """;
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile(Source);
        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleEntitiesFile);
        string patrol = generated[generated.IndexOf(" Fill_Game_Patrol(global::Game.Patrol target", StringComparison.Ordinal)..];
        patrol = patrol[..patrol.IndexOf("return target;", StringComparison.Ordinal)];
        Scene scene = GeneratorHarness.Composed(GeneratorHarness.Registry(GeneratorHarness.Loaded(compiled)), """
            {"entities": [{"type": "mover", "movement": {"type": "patrol", "speed": "slow", "reach": 4}}, {"type": "mover", "movement": {"type": "dash", "speed": 9}}]}
            """);
        object dash = Member(scene.Entities[1], "Movement")!;

        Assert.Contains("Fill_Game_Movement(target, members);", patrol, StringComparison.Ordinal);
        Assert.DoesNotContain("\"speed\"", patrol, StringComparison.Ordinal);
        Assert.Equal(("slow", 4f), ((string)Member(Member(scene.Entities[0], "Movement")!, "Speed")!, (float)Member(Member(scene.Entities[0], "Movement")!, "Reach")!));
        Assert.Equal(9f, (float)dash.GetType().GetProperty("Speed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!.GetValue(dash)!);
    }

    private static Scene Composed(string json) => GeneratorHarness.Composed(Registry.Value, json);

    private static object? Member(object owner, string name) => GeneratorHarness.Member(owner, name);
}
