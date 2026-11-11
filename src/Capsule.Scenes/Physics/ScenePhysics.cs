using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Physics;

// A scene's collision world and the bookkeeping its colliders and bodies share: the colliders that
// report contacts, the layer unions a shove searches, and how deep any body has sunk.
internal sealed class ScenePhysics
{
    private readonly SettleList<Collider2D> _contactReporters = new();

    // How many bodies in this scene are moved by each collision layer, indexed by layer.
    private readonly int[] _movedByCounts = new int[CollisionWorld2D.MaxLayers];

    // How many bodies with their collider on one layer are moved by another, indexed by the moving
    // layer and then the body's. Null until a body is moved by a layer, as most scenes' never are.
    private int[]? _movedCounts;

    // One bit per layer holding the collider of a body the indexed layer moves.
    private readonly ulong[] _movedLayers = new ulong[CollisionWorld2D.MaxLayers];

    // Read through Scene.Collision once the scene starts. The preload sizes it before then.
    internal CollisionWorld2D World { get; } = new();

    // One bit per layer some body in this scene is moved by. A collider on none of them shoves nothing.
    internal ulong MovedByLayers { get; private set; }

    // One bit per layer holding the collider of a body that a collider on `layer` moves. A shove
    // searches only these layers. A crowd it cannot move costs a pusher nothing, bodies or not.
    internal ulong LayersMovedBy(int layer) => _movedLayers[layer];

    // Counts every change to which bodies a layer moves: a body joining or leaving, its collider changing
    // layer, its moved-by layers changing. A shove that sees it move gathers its bodies again.
    internal int BodyChanges { get; private set; }

    // The deepest any body in this scene has stood below the pose it sweeps. A pusher looks this far
    // below its path for the bodies whose swept pose it crosses.
    internal float DeepestSink { get; private set; }

    internal int ContactReporterCount => _contactReporters.Count;

    // Raises every contact the step's final positions produce. A collider whose entity is held settles nothing.
    internal void SettleContacts()
    {
        _contactReporters.Begin();
        try
        {
            while (_contactReporters.TryNext(out Collider2D? collider))
            {
                if (!collider.Entity!.Held)
                {
                    collider.SettleContacts();
                }
            }
        }
        finally
        {
            _contactReporters.End();
        }
    }

    internal void TrackContacts(Collider2D collider) => _contactReporters.Add(collider);

    internal void UntrackContacts(Collider2D collider) => _contactReporters.Remove(collider);

    internal void NoteSink(float sink) => DeepestSink = MathF.Max(DeepestSink, sink);

    // Adds or withdraws one body, its collider on bodyLayer's interned index, from the layers that
    // move it.
    internal void CountBody(CollisionFilter movedBy, int bodyLayer, int delta)
    {
        BodyChanges++;
        MovedByLayers = AdjustLayerUnion(_movedByCounts, movedBy.Bits, delta, MovedByLayers);

        if (movedBy.Bits == 0)
        {
            return;
        }

        _movedCounts ??= new int[CollisionWorld2D.MaxLayers * CollisionWorld2D.MaxLayers];
        ulong bodyBit = 1UL << bodyLayer;
        for (ulong bits = movedBy.Bits; bits != 0; bits &= bits - 1)
        {
            int layer = BitOperations.TrailingZeroCount(bits);
            int count = _movedCounts[(layer * CollisionWorld2D.MaxLayers) + bodyLayer] += delta;
            _movedLayers[layer] = count > 0 ? _movedLayers[layer] | bodyBit : _movedLayers[layer] & ~bodyBit;
        }
    }

    internal void Reserve(int colliders, int grids, int reporters)
    {
        World.Reserve(colliders, grids);
        _contactReporters.Reserve(reporters);
    }

    internal void ClearContactReporters() => _contactReporters.Clear();

    // Adds delta to the count of each layer in bits, and returns union with each of those layers' bits
    // set while its count is above zero.
    private static ulong AdjustLayerUnion(int[] counts, ulong bits, int delta, ulong union)
    {
        while (bits != 0)
        {
            int layer = BitOperations.TrailingZeroCount(bits);
            ulong bit = 1UL << layer;
            bits &= bits - 1;

            counts[layer] += delta;
            union = counts[layer] > 0 ? union | bit : union & ~bit;
        }

        return union;
    }
}
