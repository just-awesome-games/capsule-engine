using System.Runtime.InteropServices;
using Capsule.Rendering;

namespace Capsule.Scenes;

public partial class Scene
{
    private readonly EntityTree _tree;
    private readonly List<Entity> _pendingAdds = [];
    private readonly List<Entity> _pendingRemoves = [];

    // Membership checked by reference identity, not by Equals.
    private readonly HashSet<Entity> _pendingAddSet = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Entity> _pendingRemoveSet = new(ReferenceEqualityComparer.Instance);

    // Attached but not started. Entities start as a batch, and each can search the scene as it starts.
    private readonly List<Entity> _pendingStarts = [];

    // The roots of the detaches landing now, nested when a removal hook starts another.
    private readonly List<Entity> _detachingRoots = [];

    // How many entities have joined under each name, which is the number the next one takes. Scene and Camera
    // name the scene's own panel and its camera's, and an entity asking for either starts at (1).
    private readonly Dictionary<string, int> _nameNumbers = new(StringComparer.Ordinal) { ["Scene"] = 1, ["Camera"] = 1 };

    private int _attaching;
    private bool _starting;

    // Set while the drain attaches its queued adds. A child landing mid-list then joins the end, and the
    // order rebuilds once after the batch.
    private bool _batchingAdds;

    // Whether a step is running, from BeginStep until EndStep finishes. A structural change waits for the drain.
    internal bool Stepping => _stepping;

    // How many slots the tree holds, vacated ones included.
    internal int SlotCount => _tree.Slots.Count;

    /// <summary>
    /// Adds a root entity and its subtree. During a step the add is deferred to the end of the
    /// step.
    /// </summary>
    /// <remarks>
    /// An entity with a parent joins through that parent and is refused here. Before the scene starts the
    /// add only composes: the subtree is in <see cref="Entities"/> and the searches, runs no hook, and
    /// joins as the scene starts. A component that refuses the scene from its entry hook leaves the entity
    /// in the scene with only the components before it registered. A deferred add raises that refusal at
    /// the drain.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The scene has stopped, the entity is already in a scene or queued, the entity has a parent,
    /// a component refused the scene, or it is called inside a <see cref="Renderer.Draw"/>.
    /// </exception>
    public void Add(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.Parent is { } parent)
        {
            throw new InvalidOperationException(
                $"A {entity.GetType().Name} is parented under a {parent.GetType().Name}. Add the root of the tree instead.");
        }

        Enqueue(entity);
    }

    /// <summary>
    /// Removes an entity and its subtree. During a step the remove is deferred and idempotent.
    /// </summary>
    /// <remarks>
    /// An entity still queued for addition attaches and detaches in the same drain, and one composed
    /// before the scene started leaves with no hook. Children detach first, deepest and last-parented
    /// first. A child removed by itself becomes a root. Every removal hook runs, and failures propagate
    /// once detachment finishes.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The scene has stopped, the entity is not in it, or it is called inside a <see cref="Renderer.Draw"/>.
    /// </exception>
    public void Remove(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ThrowIfStopped();
        Drawing.ThrowIfDrawing("removed an entity");

        if (!ReferenceEquals(entity.SceneOrNull, this) && !_pendingAddSet.Contains(entity))
        {
            throw new InvalidOperationException($"This scene does not hold the {entity.GetType().Name} being removed.");
        }

        if (_stepping)
        {
            if (_pendingRemoveSet.Add(entity))
            {
                _pendingRemoves.Add(entity);
            }

            return;
        }

        Detach(entity);
    }

    // Held and not queued for removal, by itself or with an ancestor.
    internal bool Contains(Entity entity) => ReferenceEquals(entity.SceneOrNull, this) && !IsRemovalPending(entity);

    // Whether Remove was called on the entity itself this step.
    internal bool IsRemovalRequested(Entity entity) => _pendingRemoveSet.Count > 0 && _pendingRemoveSet.Contains(entity);

    internal bool IsDetaching(Entity entity)
    {
        if (_detachingRoots.Count == 0)
        {
            return false;
        }

        for (Entity? above = entity; above is not null; above = above.Parent)
        {
            foreach (Entity root in _detachingRoots)
            {
                if (ReferenceEquals(above, root))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Whether a removal queued this step takes the entity, directly or through an ancestor.
    internal bool IsRemovalPending(Entity entity)
    {
        if (_pendingRemoveSet.Count == 0)
        {
            return false;
        }

        for (Entity? above = entity; above is not null; above = above.Parent)
        {
            if (_pendingRemoveSet.Contains(above))
            {
                return true;
            }
        }

        return false;
    }

    internal void ReserveMembers(int entities)
    {
        _tree.Reserve(entities);
        _detachingRoots.EnsureCapacity(entities);
        _pendingAdds.EnsureCapacity(entities);
        _pendingAddSet.EnsureCapacity(entities);
        _pendingStarts.EnsureCapacity(entities);
        _pendingRemoves.EnsureCapacity(entities);
        _pendingRemoveSet.EnsureCapacity(entities);
    }

    // Drain queues while lifecycle hooks grow them. Dropped entities are not retried next step.
    private void DrainPending()
    {
        if (_tree.Stale)
        {
            RebuildOrder();
        }

        while (_pendingAdds.Count > 0 || _pendingRemoves.Count > 0 || _pendingStarts.Count > 0)
        {
            int processed = 0;
            _batchingAdds = true;
            try
            {
                while (processed < _pendingAdds.Count)
                {
                    Entity pending = _pendingAdds[processed];

                    // Counted before the attach. A failed attach is then not retried.
                    processed++;
                    Attach(pending);
                }
            }
            finally
            {
                _batchingAdds = false;
                DropProcessed(_pendingAdds, _pendingAddSet, processed);

                // Before the removals and the starts, which run parent-first by slot.
                if (_tree.Stale)
                {
                    RebuildOrder();
                }
            }

            processed = 0;
            try
            {
                while (processed < _pendingRemoves.Count)
                {
                    Entity pending = _pendingRemoves[processed];
                    processed++;
                    Detach(pending);
                }
            }
            finally
            {
                DropProcessed(_pendingRemoves, _pendingRemoveSet, processed);
            }

            // Start pending after draining both queues so the batch starts together.
            StartPending();
        }
    }

    // Enqueue for addition. A child parented under a held entity joins directly.
    internal void Enqueue(Entity entity)
    {
        ThrowIfStopped();
        Drawing.ThrowIfDrawing("added an entity");

        if (entity.IdleInPool)
        {
            throw new InvalidOperationException(
                $"This {entity.GetType().Name} is idle in its pool. Take it from the pool instead of adding it.");
        }

        if (entity.SceneOrNull is not null || _pendingAddSet.Contains(entity))
        {
            throw new InvalidOperationException(
                $"A {entity.GetType().Name} is already in a scene. Remove it before adding it elsewhere.");
        }

        if (_stepping)
        {
            _pendingAdds.Add(entity);
            _pendingAddSet.Add(entity);
            entity.PendingScene = this;
            return;
        }

        Attach(entity);
    }

    private static void DropProcessed(List<Entity> queue, HashSet<Entity> membership, int processed)
    {
        for (int index = 0; index < processed; index++)
        {
            membership.Remove(queue[index]);
            queue[index].PendingScene = null;
        }

        queue.RemoveRange(0, processed);
    }

    private void Attach(Entity entity)
    {
        // Idempotent: the drain may hand over an entity that already attached.
        if (entity.SceneOrNull is not null)
        {
            return;
        }

        // A scene that has not started composes. It joins what it holds as it starts.
        if (!_started)
        {
            Compose(entity);
            return;
        }

        _attaching++;
        try
        {
            AttachTree(entity);
        }
        finally
        {
            _attaching--;
        }

        // Start immediately if attached outside a step. During a step, EndStep starts all together.
        // A join hook that adds more waits for the outermost attach, which keeps the starts parent-first.
        if (!_stepping && _attaching == 0)
        {
            StartPending();
        }
    }

    // Puts the entity and its subtree in the tree and the scene's reach, and runs no hook.
    private void Compose(Entity entity)
    {
        Place(entity);
        foreach (Entity child in entity.Children)
        {
            if (child.SceneOrNull is null)
            {
                Compose(child);
            }
        }
    }

    private void Place(Entity entity)
    {
        _tree.Insert(entity, _batchingAdds);
        entity.SceneOrNull = this;
        entity.PendingScene = null;
    }

    // Joins every entity composed before the scene started, in tree order. A hook that adds attaches
    // that entity at once, and one that removes a composed entity before its turn leaves it unjoined.
    private void JoinComposed()
    {
        _tree.Compact();
        _attaching++;
        try
        {
            List<Entity> slots = _tree.Slots;
            int index = 0;
            while (index < slots.Count)
            {
                if (slots[index] is not { Joined: false } composed)
                {
                    index++;
                    continue;
                }

                int version = _tree.Version;
                JoinTree(composed);

                // A hook that moved entities may have shifted ones still waiting to an earlier slot.
                index = version == _tree.Version ? index + 1 : 0;
            }
        }
        finally
        {
            _attaching--;
        }
    }

    // The number the next entity joining under name takes, 0 for the first, whose name stays bare.
    internal int NextNameNumber(string name)
    {
        ref int next = ref CollectionsMarshal.GetValueRefOrAddDefault(_nameNumbers, name, out _);

        return next++;
    }

    // Holds a place for name at preload, where the scene may allocate. Its first join then allocates
    // nothing.
    internal void ReserveName(string name) => _nameNumbers.TryAdd(name, 0);

    private void AttachTree(Entity entity)
    {
        Place(entity);
        JoinTree(entity);
    }

    // Joins an entity the scene already holds, then its children. A child is placed first if the scene
    // does not hold it yet.
    private void JoinTree(Entity entity)
    {
        entity.Joined = true;
        entity.NameNumber = NextNameNumber(entity.RequestedName);
        Drawing.Invalidate();

        // Resolved against this step's state. A join during the step then settles its screen notifiers
        // as the rest of the tree does.
        entity.ResolveHold(_pausedThisStep, _frozenThisStep);

        // A join never interpolates. The parent saves first, and a child's World composes against a
        // parent already collapsed onto its current transform.
        entity.SavePrevious();

        // Enter components before the entity hook. Components attached from OnAddedToScene notify separately.
        entity.EnterScene();
        entity.OnAddedToScene();

        _pendingStarts.Add(entity);

        // A hook may add or remove a child. The index advances only while its slot still holds the one just visited.
        // A hook that removed this entity, or an ancestor, ends the walk. The children left with it.
        int child = 0;
        while (ReferenceEquals(entity.SceneOrNull, this) && child < entity.Children.Length)
        {
            Entity next = entity.Children[child];
            if (!next.Joined)
            {
                if (next.SceneOrNull is null)
                {
                    Place(next);
                }

                JoinTree(next);
            }

            if (child < entity.Children.Length && ReferenceEquals(entity.Children[child], next))
            {
                child++;
            }
        }
    }

    // The step walk reads the list. A rebuild during it waits for the drain.
    internal void Reparented(Entity entity, bool wasRoot)
    {
        InvalidateHolds();
        _tree.Reparented(entity, wasRoot);

        if (!_stepping || _drained)
        {
            RebuildOrder();
        }
    }

    // A join hook inside the drain's add batch reads a list that mid-list joins left stale. No walk
    // runs then, and the read rebuilds it into the order an eager insert would have kept.
    private void SettleOrder()
    {
        if (_batchingAdds && _tree.Stale)
        {
            RebuildOrder();
        }
    }

    private void RebuildOrder()
    {
        _tree.Rebuild();
        Drawing.Invalidate();
    }

    // OnStart may attach more, and this loop drains those too.
    private void StartPending()
    {
        if (_starting)
        {
            return;
        }

        _starting = true;

        try
        {
            int processed = 0;
            int sortedVersion = -1;
            try
            {
                while (processed < _pendingStarts.Count)
                {
                    // Joins, rebuilds and hooks move entities that wait, and starts run parent-first. A slot
                    // moves only with the version. A batch with no moves sorts once.
                    if (sortedVersion != _tree.Version)
                    {
                        sortedVersion = _tree.Version;
                        if (_pendingStarts.Count - processed > 1)
                        {
                            CollectionsMarshal.AsSpan(_pendingStarts)[processed..].Sort(static (left, right) => left.SceneSlot.CompareTo(right.SceneSlot));
                        }
                    }

                    Entity pending = _pendingStarts[processed];
                    processed++;

                    // Skip entities that attached and detached or were removed before their turn.
                    if (Contains(pending))
                    {
                        pending.RunStart();
                    }
                }

                // A join never interpolates, wherever the batch's start hooks left each entity. The
                // whole batch has started first, which covers a start that moved a peer.
                for (int index = 0; index < processed; index++)
                {
                    if (Contains(_pendingStarts[index]))
                    {
                        _pendingStarts[index].SavePrevious();
                    }
                }
            }
            finally
            {
                _pendingStarts.RemoveRange(0, processed);
            }
        }
        finally
        {
            _starting = false;
        }
    }

    // Detach the entity and release its parent so it can be reparented or added as a root.
    private void Detach(Entity entity)
    {
        _detachingRoots.Add(entity);
        try
        {
            DetachTree(entity);
            entity.Unparent();
        }
        finally
        {
            _detachingRoots.RemoveAt(_detachingRoots.Count - 1);
        }
    }

    // Detach children before the entity, deepest and last-parented first. A hook may reparent, and
    // the list is re-read on every pass.
    private void DetachTree(Entity entity)
    {
        for (int child = entity.Children.Length - 1; child >= 0; child--)
        {
            if (child < entity.Children.Length)
            {
                DetachTree(entity.Children[child]);
            }
        }

        if (ReferenceEquals(entity.SceneOrNull, this))
        {
            DetachAt(entity.SceneSlot);
        }
    }

    private void DetachAt(int index)
    {
        Entity entity = _tree.Vacate(index);

        Drawing.Invalidate();
        bool joined = entity.Joined;
        entity.Leaving = true;
        entity.SceneOrNull = null;
        entity.Joined = false;

        try
        {
            // An entity composed before the scene started and never joined has no hook to run.
            if (joined)
            {
                entity.LeaveScene();
            }
        }
        finally
        {
            entity.Leaving = false;

            // Returns even when a removal hook throws. A hook that re-added the entity leaves it
            // taken, and it returns on its next detach.
            if (entity.Pool is { } pool && entity.SceneOrNull is null && entity.PendingScene is null)
            {
                pool.Return(entity);
            }
        }
    }

    private void ReleaseEntities(ref List<Exception>? failures)
    {
        // A removal hook may read the scene and compact it. Each pass re-checks the slot.
        List<Entity> slots = _tree.Slots;
        for (int index = slots.Count - 1; index >= 0; index--)
        {
            if (index >= slots.Count || slots[index] is null)
            {
                continue;
            }

            try
            {
                DetachAt(index);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        _tree.Compact();
    }

    private void Release(List<Exception>? failures)
    {
        ReleaseEntities(ref failures);
        ClearPendingState();
        ThrowCleanupFailures(failures);
    }

    private void ClearPendingState()
    {
        _pendingStarts.Clear();
        _pendingAdds.Clear();
        _pendingAddSet.Clear();
        _pendingRemoves.Clear();
        _pendingRemoveSet.Clear();
        _tree.Reset();
        Drawing.Clear();
        Physics.ClearContactReporters();
        Visibility.Clear();
    }

    private void ThrowIfStopped()
    {
        if (_stopped)
        {
            throw new InvalidOperationException($"A stopped {GetType().Name} cannot be changed or request another transition.");
        }
    }
}
