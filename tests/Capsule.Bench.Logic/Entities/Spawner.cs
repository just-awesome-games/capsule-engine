using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>
/// Removes the motes whose time is up and places 34 more from a pool every step, each to live 30
/// steps, so about two thousand join and leave a second and the population holds.
/// </summary>
public sealed class Spawner : Entity
{
    private const int PerStep = 34;

    private const int LifeSteps = 30;

    private readonly Queue<Mote> _live = new((LifeSteps + 2) * PerStep);
    private readonly EntityPool<Mote> _pool = new(() => new Mote(), (LifeSteps + 2) * PerStep);
    private int _placed;

    public Spawner()
        : base(Vector2.Zero)
    {
    }

    protected override void OnStep(in StepContext context)
    {
        while (_live.TryPeek(out Mote? oldest) && oldest.DiesAt <= context.Tick)
        {
            Scene!.Remove(_live.Dequeue());
        }

        for (int index = 0; index < PerStep; index++)
        {
            Mote mote = _pool.Take();
            mote.Position = new Vector2((_placed * 37) % (int)World.ViewportSize.X, (_placed * 53) % (int)World.ViewportSize.Y);
            mote.DiesAt = context.Tick + LifeSteps;
            _placed++;

            Scene!.Add(mote);
            _live.Enqueue(mote);
        }
    }
}
