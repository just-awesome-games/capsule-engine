using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

internal static class SceneHostFixtures
{
    internal static SceneTransition ToScene<TScene>(object? payload = null)
        where TScene : Scene
        => SceneTransition.ToScene(typeof(TScene), payload);

    /// <summary>Requests the document "boss-room", the named target these specs resolve.</summary>
    internal static void RequestsBossRoom(Scene scene, in StepContext context) =>
        scene.Run.RequestScene("boss-room");

    /// <summary>Hands over to <see cref="SecondScene"/> with a payload on its first step.</summary>
    internal sealed class FirstScene(List<string> log) : Scene
    {
        protected override void OnStart() => log.Add("first.start");

        protected override void OnStep(in StepContext context)
        {
            log.Add("first.step");
            Run.RequestScene<SecondScene>("handoff");
        }

        protected override void OnStop() => log.Add("first.stop");
    }

    /// <summary>Records the payload it was entered with.</summary>
    internal sealed class SecondScene(List<string> log) : Scene
    {
        internal object? ReceivedPayload { get; private set; }

        protected override void OnStart()
        {
            ReceivedPayload = EntryPayload;
            log.Add("second.start");
        }

        protected override void OnStep(in StepContext context) => log.Add($"second.step:{context.Tick}");
    }
}
