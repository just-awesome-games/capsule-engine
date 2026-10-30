using Capsule.Scenes;

namespace Capsule.Tests;

// A game object that runs what a spec hands it at run start and end, for a spec whose subject is
// what the game sets up, not the game object itself.
internal sealed class CallbackGame(Run run, Action<Run>? start, Action<Run>? end) : GameInstance(run)
{
    internal static Func<Run, GameInstance> Starting(Action<Run> start) => run => new CallbackGame(run, start, null);

    internal static Func<Run, GameInstance> Ending(Action<Run> end) => run => new CallbackGame(run, null, end);

    protected override void OnStart() => start?.Invoke(Run);

    protected override void OnEnd() => end?.Invoke(Run);
}
