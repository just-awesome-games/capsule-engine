using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// The entities that run a test's hook from one of their own.
internal static partial class SceneFixtures
{
    internal sealed class Meddler(Action<Scene> onAdded) : Entity(Vector2.Zero)
    {
        protected internal override void OnAddedToScene() => onAdded(Scene!);
    }

    /// <summary>Runs <see cref="OnLeave"/> as it leaves its scene.</summary>
    internal sealed class Leaver() : Entity(Vector2.Zero)
    {
        internal Action? OnLeave { get; set; }

        protected internal override void OnRemovedFromScene() => OnLeave?.Invoke();
    }

    /// <summary>Runs a hook the moment time begins for it, when the scene is fully composed.</summary>
    internal sealed class Starter(Action<Scene> onStart) : Entity(Vector2.Zero)
    {
        protected internal override void OnStart() => onStart(Scene!);
    }

    internal sealed class Watcher(Action<Scene> observe) : Entity(Vector2.Zero)
    {
        protected internal override void OnStep(in StepContext context) => observe(Scene!);
    }
}
