using System.Numerics;
using Capsule.Assets;
using Capsule.Assets.Generated;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Components;
using Capsule.Bench.Logic.Entities;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// Scene transitions with media at every boundary. Every 120 steps the run restarts this scene with
/// the other half as its payload, and each half draws a texture the other never uses, so the
/// outgoing texture is released and the incoming one loaded on each turn — one class with a payload
/// rather than two, so no half is a workload of its own. The signal is the boundary frame:
/// <c>drawMs</c> max and the tail of <c>intervalMs</c>.
/// </summary>
[Workload(WorkloadKind.Rendering)]
public class Transition : Scene
{
    private readonly TextureHandle _first;
    private readonly TextureHandle _second;
    private readonly int _regionSize;
    private readonly float _extent;
    private readonly Vector2 _viewport;
    private readonly int _count;
    private bool _onSecond;

    public Transition()
        : this(CapsuleAssets.Textures.TransitionA, CapsuleAssets.Textures.TransitionB, 64, 16f, World.ViewportSize, 400)
    {
    }

    protected Transition(TextureHandle first, TextureHandle second, int regionSize, float extent, Vector2 viewport, int count)
    {
        _first = first;
        _second = second;
        _regionSize = regionSize;
        _extent = extent;
        _viewport = viewport;
        _count = count;
        Camera = new ParkedCamera(viewport / 2f, viewport);
    }

    // The payload is handed over at start, not construction; the first boot carries none.
    protected override void OnStart()
    {
        _onSecond = EntryPayload is true;
        Sprite frame = new(_onSecond ? _second : _first, new TextureRegion(0, 0, _regionSize, _regionSize), new Vector2(_regionSize / 2f, _regionSize / 2f));

        Add(new Holder(new SpriteField(frame, _extent, _viewport, _count)));
    }

    protected override void OnStep(in StepContext context)
    {
        if (context.Tick % 120 == 119)
        {
            Run.RequestRestart(!_onSecond);
        }
    }
}
