using System.Numerics;
using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>
/// A drifting body whose visual child animates a frame box on and off, facing by a negative scale on
/// every other one.
/// </summary>
/// <remarks>The box reports no contacts. It measures placement and the collision world's churn alone.</remarks>
public sealed class Striker : Entity
{
    // Steps each way before the drift turns.
    private const int Leg = 60;

    private readonly float _drift;
    private int _steps;

    public Striker(Vector2 position, int index)
        : base(position)
    {
        _drift = index % 2 == 0 ? 0.25f : -0.25f;

        Entity visual = new(this) { Scale = new Vector2(index % 2 == 0 ? 1f : -1f, 1f) };

        SpriteRenderer renderer = new(CapsuleAssets.Sprites.DancerSheet.Frames.Dance0);
        visual.Add(renderer);
        renderer.Box(CapsuleAssets.Sprites.DancerSheet.Boxes.Strike).Layer = CollisionLayers.Actor;

        SpriteAnimator animator = new(renderer);
        visual.Add(animator);
        animator.Play(CapsuleAssets.Sprites.DancerSheet.Clips.Dance, atTick: index % 12);
    }

    // Every move re-places the box under the moved root.
    protected override void OnStep(in StepContext context)
    {
        Position += new Vector2(_steps / Leg % 2 == 0 ? _drift : -_drift, 0f);
        _steps++;
    }
}
