using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Tests.DevTools;

// The scenes and registry the overlay specs run over: one scene that counts its steps, two more to
// transition to, and the two that refuse a transition.
internal static class OverlayFixtures
{
    internal const string NamedDocument = "levels/named";

    // A held run's window, wider than the 16:9 viewport so a fit other than Letterbox grows it.
    internal static readonly Vector2 Output = new(1000f, 500f);

    // A run of one scene whose frame the scheduler places on Output.
    internal static OverlayRig Framing(Scene scene, SceneRegistry? registry = null)
    {
        OverlayRig rig = new(new SceneHost(SceneTransition.ToScene(scene.GetType(), null), (in SceneTransition _) => scene, new Run()), registry);
        rig.Scheduler.Output = Output;

        return rig;
    }

    internal static SceneHost CreateHost(Run? run = null, List<SceneTransition>? resolved = null) =>
        new(
            SceneTransition.ToScene(typeof(ReadoutScene), null),
            (in SceneTransition target) =>
            {
                resolved?.Add(target);

                return target.Kind switch
                {
                    SceneTransitionKind.Named => new NamedScene(),
                    SceneTransitionKind.Scene when target.SceneType == typeof(PlainScene) => new PlainScene(),
                    SceneTransitionKind.Scene when target.SceneType == typeof(PayloadScene) => new PayloadScene(),
                    SceneTransitionKind.Scene when target.SceneType == typeof(ReadoutScene) => new ReadoutScene(),
                    _ => throw new InvalidOperationException($"Unexpected transition {target.Kind}."),
                };
            },
            run ?? new Run());

    internal static SceneRegistry CreateRegistry() =>
        new(
            new EntityRegistry([]),
            [
                SceneRegistration.Plain(typeof(PlainScene), static _ => new PlainScene()),
                SceneRegistration.Plain(typeof(PayloadScene), static _ => new PayloadScene()),
                SceneRegistration.FromDocument(typeof(NamedScene), NamedDocument, static _ => new NamedScene()),
            ]);

    internal sealed class ReadoutScene : Scene
    {
        internal int Steps { get; private set; }

        internal bool Stopped { get; private set; }

        protected override void OnStep(in StepContext context) => Steps++;

        protected override void OnStop() => Stopped = true;
    }

    internal sealed class PlainScene : Scene;

    // A camera following a walker inside bounds, moved by a standing offset. Before its first step the
    // frame carries the camera's unconfined centre, which the bounds confine.
    internal sealed class FramedScene : Scene
    {
        internal Entity Walker { get; } = new Striding();

        internal FramedScene()
            : this(ViewportFit.Letterbox)
        {
        }

        internal FramedScene(ViewportFit fit)
        {
            Camera.ViewportSize = new Vector2(320f, 180f);
            Camera.Fit = fit;
            Camera.Bounds = Rect.FromEdges(0f, 0f, 400f, 300f);
            Camera.Center = new Vector2(-100f, 500f);
            Camera.Offset = new Vector2(3f, -2f);
            Add(Walker);
        }

        protected override void OnStart() => Camera.Follow(Walker);

        private sealed class Striding : Entity
        {
            internal Striding()
                : base(new Vector2(100f, 100f)) => Add(new ColorRect(new Vector2(8f, 8f)));

            protected internal override void OnStep(in StepContext context) => Position += new Vector2(7f, 3f);
        }
    }

    internal sealed class NamedScene : Scene;

    internal sealed class ExitOnStartScene : Scene
    {
        protected override void OnStart() => Run.RequestExit();
    }

    internal sealed class RequestOnStartScene : Scene
    {
        protected override void OnStart() => Run.RequestScene<PlainScene>();
    }

    // Refuses to start without a payload, which is how a load that fails mid-tick is provoked.
    internal sealed class PayloadScene : Scene
    {
        protected override void OnStart()
        {
            if (EntryPayload is null)
            {
                throw new InvalidOperationException("PayloadScene needs a payload.\nSecond line.");
            }
        }
    }
}
