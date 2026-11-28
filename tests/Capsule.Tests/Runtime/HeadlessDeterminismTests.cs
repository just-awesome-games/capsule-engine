using System.Numerics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Scenes;

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

        HeadlessRunResult result = QuietEngine.Configure([SceneRegistration.Plain(typeof(Wandering), _ => scene)])
            .WithGame(CallbackGame.Starting(static run => run.Input.Bind(Nudge, Key.Space)))
            .RunHeadless<Wandering>(new InputScript().Wait(3).Tap(Key.Space).Wait(4).Build());

        return (result.Steps, [.. scene.Entities.ToArray().Select(static entity => entity.Position)]);
    }

    private sealed class Wandering : Scene
    {
        protected override void OnStart()
        {
            for (int index = 0; index < 3; index++)
            {
                Add(new Wanderer(Nudge));
            }
        }
    }
}
