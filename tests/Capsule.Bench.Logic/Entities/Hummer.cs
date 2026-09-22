using System.Numerics;
using Capsule.Audio;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

// A looping voice whose volume and pan are rewritten every step, so the mixer has a command to
// generate for it on every one.
public sealed class Hummer : Entity
{
    private const int SweepPeriod = 90;

    private static readonly AudioBus Ambience = new("ambience");

    private static readonly AudioBus Sfx = new("sfx");

    private readonly AudioSource _source;
    private readonly int _phase;

    public Hummer(Vector2 position, int index)
        : base(position)
    {
        _phase = index * 7;
        _source = new AudioSource(CapsuleAssets.Audio.Hum)
        {
            Bus = index % 2 == 0 ? Ambience : Sfx,
            Loop = true,
            PlayOnStart = true,
        };
        Add(_source);
    }

    protected override void OnStep(in StepContext context)
    {
        long phase = (context.Tick + _phase) % SweepPeriod;
        float sweep = (phase < SweepPeriod / 2 ? phase : SweepPeriod - phase) / (float)(SweepPeriod / 2);

        _source.Volume = 0.2f + (0.6f * sweep);
        _source.Pan = (2f * sweep) - 1f;
    }
}
