using System.Numerics;
using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>An animated sprite that never moves, playing at a speed its index picks and reading its events after each step.</summary>
public sealed class Dancer : Entity
{
    // Whole, fractional and above one, so the carry and multi-frame steps both run.
    private static readonly float[] Speeds = [1f, 0.5f, 1.5f, 2.37f, 0.75f];

    private readonly SpriteAnimator _animator;

    /// <summary>How many steps this dancer has read a step event on.</summary>
    public int Steps { get; private set; }

    public Dancer(Vector2 position, int index)
        : base(position)
    {
        SpriteRenderer renderer = new(CapsuleAssets.Sprites.DancerSheet.Frames.Dance0);
        Add(renderer);

        _animator = new SpriteAnimator(renderer) { Speed = Speeds[index % Speeds.Length] };
        Add(_animator);

        // Started part-way through the cycle, so the crowd's frame changes spread over every step.
        _animator.Play(CapsuleAssets.Sprites.DancerSheet.Clips.Dance, atTick: index % 12);
    }

    protected override void OnLateStep(in StepContext context)
    {
        if (_animator.Reached(CapsuleAssets.Sprites.DancerSheet.Events.Step))
        {
            Steps++;
        }
    }
}
