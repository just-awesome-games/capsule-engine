using Capsule.Assets;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

internal static class SceneHostFixtures
{
    internal static SceneTransition ToScene<TScene>(IScenePayload? payload = null)
        where TScene : Scene
        => SceneTransition.ToScene(typeof(TScene), payload);

    /// <summary>Requests the document "target-room", the named target these specs resolve.</summary>
    internal static void RequestsNamedTarget(Scene scene, in StepContext context) =>
        scene.Run.RequestScene(new SceneKey("target-room"));

    /// <summary>A payload these specs tell apart by its name.</summary>
    internal sealed record Tag(string Name) : IScenePayload;

    /// <summary>
    /// Declares the texture its <see cref="Tag"/> payload names, and nothing without one. It records the payload
    /// name each collection read.
    /// </summary>
    internal sealed class DeclaresFromPayload : Scene
    {
        internal List<string?> Collected { get; } = [];

        protected internal override void CollectAssets(AssetCollection assets)
        {
            string? name = (EntryPayload as Tag)?.Name;
            Collected.Add(name);
            if (name is not null)
            {
                assets.Add(new TextureHandle(name, ".png"));
            }
        }
    }

    /// <summary>Hands over to <see cref="SecondScene"/> with a payload on its first step.</summary>
    internal sealed class FirstScene(List<string> log) : Scene
    {
        protected override void OnStart() => log.Add("first.start");

        protected override void OnStep(in StepContext context)
        {
            log.Add("first.step");
            Run.RequestScene<SecondScene>(new Tag("handoff"));
        }

        protected override void OnStop() => log.Add("first.stop");
    }

    /// <summary>Records the payload it was entered with.</summary>
    internal sealed class SecondScene(List<string> log) : Scene
    {
        internal IScenePayload? ReceivedPayload { get; private set; }

        protected override void OnStart()
        {
            ReceivedPayload = EntryPayload;
            log.Add("second.start");
        }

        protected override void OnStep(in StepContext context) => log.Add($"second.step:{context.Tick}");
    }
}
