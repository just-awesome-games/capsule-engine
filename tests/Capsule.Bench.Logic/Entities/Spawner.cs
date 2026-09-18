using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>
/// Removes the motes whose time is up and places 34 more from a pool every step, each to live 30
/// steps, so about two thousand join and leave a second and the population holds. The pool is a
/// queue two steps deeper than the population: a mote removed this step is detached only when the
/// step's deferred changes drain, and must not be handed out again before then.
/// </summary>
public sealed class Spawner : Entity
{
    private const int PerStep = 34;

    private const int LifeSteps = 30;

    private readonly Queue<Mote> _live = new((LifeSteps + 2) * PerStep);
    private readonly Queue<Mote> _pool = new((LifeSteps + 2) * PerStep);
    private int _placed;

    public Spawner()
        : base(Vector2.Zero)
    {
        for (int index = 0; index < (LifeSteps + 2) * PerStep; index++)
        {
            _pool.Enqueue(new Mote());
        }
    }

    protected override void OnStep(in StepContext context)
    {
        while (_live.TryPeek(out Mote? oldest) && oldest.DiesAt <= context.Tick)
        {
            Scene!.Remove(_live.Dequeue());
            _pool.Enqueue(oldest);
        }

        for (int index = 0; index < PerStep; index++)
        {
            Mote mote = _pool.Dequeue();
            mote.Teleport(new Vector2((_placed * 37) % (int)World.ViewportSize.X, (_placed * 53) % (int)World.ViewportSize.Y));
            mote.DiesAt = context.Tick + LifeSteps;
            _placed++;

            Scene!.Add(mote);
            _live.Enqueue(mote);
        }
    }
}
