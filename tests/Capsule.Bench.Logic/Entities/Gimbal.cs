using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A root with three arms nested one under the next; it turns and its scale breathes every step, so every transform beneath it recomposes.</summary>
public sealed class Gimbal : Entity
{
    private const int BreathPeriod = 120;

    private readonly float _spin;

    public Gimbal(Vector2 position, int index)
        : base(position)
    {
        _spin = 0.01f + ((index % 7) * 0.003f);

        Entity parent = this;
        for (int level = 0; level < 3; level++)
        {
            parent = new Arm(parent);
        }
    }

    protected override void OnStep(in StepContext context)
    {
        Rotation += _spin;

        long phase = context.Tick % BreathPeriod;
        float breath = (phase < BreathPeriod / 2 ? phase : BreathPeriod - phase) / (float)(BreathPeriod / 2);
        Scale = new Vector2(0.8f + (0.4f * breath));
    }
}
