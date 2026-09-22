using System.Numerics;
using Capsule.Assets;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Components;
using Capsule.Bench.Logic.Entities;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// Scene transitions with media at every boundary. The workload hands over to its first half, and
/// every 120 steps each half requests the other. Each half declares a page the other never uses and
/// prefetches the other on arrival, so every boundary adopts the warmed page and releases the outgoing
/// one. The signal is <c>intervalMs</c> max, because loading runs outside <c>FrameRenderer.Draw</c>.
/// </summary>
[Workload(WorkloadKind.Rendering)]
public sealed class Transition : Scene
{
    protected override void OnStep(in StepContext context) => Run.RequestScene<PageA>();

    /// <summary>The half drawing <c>transition-a</c>.</summary>
    public sealed class PageA() : TransitionHalf<PageB>(CapsuleAssets.Textures.TransitionA, 64, 16f, World.ViewportSize, 400);

    /// <summary>The half drawing <c>transition-b</c>.</summary>
    public sealed class PageB() : TransitionHalf<PageA>(CapsuleAssets.Textures.TransitionB, 64, 16f, World.ViewportSize, 400);
}

/// <summary>One half of a transition workload: a field of one page, declared at construction, that prefetches <typeparamref name="TNext"/> on arrival and requests it every 120 steps.</summary>
public abstract class TransitionHalf<TNext> : Scene
    where TNext : Scene
{
    private readonly TextureHandle _page;

    protected TransitionHalf(TextureHandle page, int regionSize, float extent, Vector2 viewport, int count)
    {
        _page = page;
        Camera = new ParkedCamera(viewport / 2f, viewport);
        Sprite frame = new(page, new TextureRegion(0, 0, regionSize, regionSize), new Vector2(regionSize / 2f, regionSize / 2f));
        Add(new Holder(new SpriteField(frame, extent, viewport, count)));
    }

    protected override void CollectAssets(AssetCollection assets) => assets.Add(_page);

    protected override void OnStart() => Run.PrefetchScene<TNext>();

    protected override void OnStep(in StepContext context)
    {
        if (context.Tick % 120 == 119)
        {
            Run.RequestScene<TNext>();
        }
    }
}
