using System.Numerics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class HeadlessDeterminismTests
{
    private static readonly InputAction Nudge = new("Nudge");

    // Pins replayability: one script over one seeded run is the same run every time, which is what
    // lets a headless run stand in for a playtest.
    [Fact]
    public void TwoHeadlessRunsOfOneScript_EndOnTheSameStepWithTheSamePositions()
    {
        (long Steps, Vector2[] Positions) first = Play();
        (long Steps, Vector2[] Positions) second = Play();

        Assert.Equal(first.Steps, second.Steps);
        Assert.Equal(first.Positions, second.Positions);
    }

    private static (long Steps, Vector2[] Positions) Play()
    {
        Wandering scene = new();

        HeadlessRunResult result = CapsuleEngine.Configure(
                "Deterministic Game",
                new DesktopPlatform(),
                new SceneRegistry(
                    new EntityRegistry([]),
                    [SceneRegistration.Plain(typeof(Wandering), _ => scene)]))
            .WithFixedStep(10)
            .WithInput(static input => input.Bindings.Bind(Nudge, Key.Space))
            .WithoutCrashLog()
            .WithoutLogging()
            .RunHeadless<Wandering>(new InputScript().Wait(3).Tap(Key.Space).Wait(4).Build());

        return (result.Steps, [.. scene.Entities.ToArray().Select(static entity => entity.Position)]);
    }

    private sealed class Wandering : Scene
    {
        protected override void OnStart()
        {
            for (int index = 0; index < 3; index++)
            {
                Add(new Wanderer());
            }
        }
    }

    // Steps by a draw from the run's source, so a position holds only if the whole run replays.
    private sealed class Wanderer() : Entity(Vector2.Zero)
    {
        protected internal override void OnStep(in StepContext context) =>
            Teleport(Position + new Vector2(Random.Range(-1f, 1f), context.Input.WasPressed(Nudge) ? 1f : 0f));
    }
}
