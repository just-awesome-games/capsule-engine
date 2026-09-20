using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class RunStateTests
{
    [Fact]
    public void AnAttachedObject_IsTheSameInstanceFromEveryScene_AcrossATransition()
    {
        GameState state = new();
        Run run = new();
        run.Attach(state);

        static Scene Resolve(in SceneTransition target) =>
            target.SceneType == typeof(SecondScene) ? new SecondScene() : new FirstScene();

        using SceneHost host = new(SceneTransition.ToScene(typeof(FirstScene), null), Resolve, run);
        host.Step(SceneFixtures.Step(0));

        Assert.Same(state, ((FirstScene)host.Scene).Seen);

        run.RequestScene<SecondScene>();
        host.Step(SceneFixtures.Step(1));

        Assert.Same(state, ((SecondScene)host.Scene).Seen);
    }

    [Fact]
    public void ThrowsWithNothingAttached_OnASecondAttach_AndOnTheWrongType()
    {
        Run run = new();

        Assert.Throws<InvalidOperationException>(() => run.State<GameState>());

        run.Attach(new GameState());

        Assert.Throws<InvalidOperationException>(() => run.Attach(new GameState()));
        Assert.Throws<InvalidOperationException>(() => run.State<OtherState>());
    }

    private sealed class GameState;

    private sealed class OtherState;

    private sealed class FirstScene : Scene
    {
        internal GameState? Seen { get; private set; }

        protected override void OnStart() => Seen = Run.State<GameState>();
    }

    private sealed class SecondScene : Scene
    {
        internal GameState? Seen { get; private set; }

        protected override void OnStart() => Seen = Run.State<GameState>();
    }
}
