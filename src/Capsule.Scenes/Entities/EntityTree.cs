using System.Runtime.InteropServices;

namespace Capsule.Scenes;

// A scene's held entities in tree order: each root in insertion order, then its subtree depth-first in
// parenting order. The order itself is a chain through the entities, each carrying a label that rises
// along it. An entity joins anywhere without moving another, and two labels compare two places in the
// order. The flat list is what a walk reads, and it catches up with the chain before it is read in order.
internal sealed class EntityTree(Scene scene)
{
    // How far past the last label an entity joining the end takes its own.
    private const long EndGap = 1L << 32;

    // The most of the gap an entity joining between two others takes, and it takes a sixteenth of a
    // gap narrower than sixteen of these. Children mostly join one after another ahead of the same
    // follower, and a small step leaves that gap room for many of them.
    private const long Step = 1L << 16;

    private readonly List<Entity> _entities = [];
    private readonly List<Entity> _roots = [];

    // Where a settle keeps the arrivals that land below the sorted run while it moves that run up.
    private readonly List<Entity> _arrived = [];

    // The ends of the chain.
    private Entity? _first;
    private Entity? _last;

    // How many slots at the front of _entities stand in label order, holes among them. The rest joined
    // since the list last settled, in the order they joined.
    private int _sorted;

    // Null slots a detach left in _entities, and the lowest of them. The list settles before it is
    // next read in order. A batch of removals then costs one pass instead of one shift each.
    private int _holes;
    private int _firstHole;

    // Entries in _roots that no longer name a root, or name one a newer entry also names. A leaving root
    // costs nothing but this count. They are dropped once they pass half the list. A root joining or
    // leaving costs O(1) amortized.
    private int _deadRoots;

    // Bumped whenever _entities changes. A walk that sees it change throws.
    private int _version;

    // Bumped whenever a held entity moves to another slot of _entities, which an entity joining moves
    // none to.
    private int _moves;

    // Bumped whenever a rebuild gives the held entities a new order.
    private int _reorders;

    // A parent write moved something and neither the chain nor the list follows the links.
    private bool _stale;

    // Every slot, null where a detach left a hole. A reader that needs tree order calls Settle first.
    internal List<Entity> Slots => _entities;

    internal ReadOnlySpan<Entity> Held
    {
        get
        {
            Settle();
            return CollectionsMarshal.AsSpan(_entities);
        }
    }

    // The first entity in tree order, each one's OrderNext leading to the next.
    internal Entity? First => _first;

    internal int Version => _version;

    internal int Moves => _moves;

    internal int Reorders => _reorders;

    internal bool Stale => _stale;

    internal void Reserve(int entities)
    {
        _entities.EnsureCapacity(entities);

        // Dead entries make the root list up to twice the live roots before it compacts.
        _roots.EnsureCapacity(entities * 2);
    }

    // Joins the chain after the held subtree of its nearest earlier held sibling, else right after its
    // parent. An entity no held parent places is a root and joins the end. No other entity moves.
    internal void Insert(Entity entity)
    {
        bool placed = PlacedByParent(entity);

        // A stale chain places nothing. The rebuild will.
        Entity? before = placed && !_stale ? Before(entity) : _last;
        Entity? after = before is null ? _first : before.OrderNext;
        if (after is null ? before is not null && before.Order > long.MaxValue - EndGap : after.Order - before!.Order < 2)
        {
            Respace();
            before = placed ? Before(entity) : _last;
            after = before is null ? _first : before.OrderNext;
        }

        entity.Order = after is null
            ? (before?.Order ?? 0) + EndGap
            : before!.Order + Math.Min(Math.Max((after.Order - before.Order) >> 4, 1), Step);
        Link(before, entity, after);

        // An entity joining the end of a settled list leaves it settled.
        if (after is null && _sorted == _entities.Count)
        {
            _sorted++;
        }

        entity.SceneSlot = _entities.Count;
        _entities.Add(entity);
        _version++;

        if (!placed)
        {
            AddRoot(entity);
        }
    }

    internal Entity Vacate(int index)
    {
        Entity entity = _entities[index];
        _entities[index] = null!;
        _firstHole = _holes++ == 0 ? index : Math.Min(_firstHole, index);

        Link(entity.OrderPrev, entity.OrderNext);
        entity.OrderPrev = null;
        entity.OrderNext = null;

        if (!PlacedByParent(entity))
        {
            _deadRoots++;
        }

        return entity;
    }

    // A held root written to a parent leaves the roots, and a held entity written to none joins their end.
    // Either way the order no longer follows the links.
    internal void Reparented(Entity entity, bool wasRoot)
    {
        if (wasRoot)
        {
            _deadRoots++;
        }

        if (entity.Parent is null)
        {
            AddRoot(entity);
        }

        _stale = true;
    }

    // Refills the chain and the list in tree order from the roots and the links.
    internal void Rebuild()
    {
        CompactRoots();
        _entities.Clear();
        _first = null;
        _last = null;
        foreach (Entity root in CollectionsMarshal.AsSpan(_roots))
        {
            AppendSubtree(root);
        }

        _sorted = _entities.Count;
        _holes = 0;
        _stale = false;
        _version++;
        _moves++;
        _reorders++;
    }

    // Closes the holes detaches left and merges in the entities that joined since, which leaves the list
    // in tree order, and renumbers what moved.
    internal void Settle()
    {
        if (_holes == 0 && _sorted == _entities.Count)
        {
            return;
        }

        int sorted = _sorted;
        if (_holes > 0)
        {
            Span<Entity> held = CollectionsMarshal.AsSpan(_entities);
            int kept = _firstHole;
            sorted = Math.Min(sorted, kept);
            for (int index = kept + 1; index < held.Length; index++)
            {
                if (held[index] is { } entity)
                {
                    entity.SceneSlot = kept;
                    held[kept++] = entity;
                    if (index < _sorted)
                    {
                        sorted = kept;
                    }
                }
            }

            _entities.RemoveRange(kept, _entities.Count - kept);
            _holes = 0;

            if (_deadRoots * 2 > _roots.Count)
            {
                CompactRoots();
            }
        }

        if (sorted < _entities.Count)
        {
            Merge(sorted);
        }

        _sorted = _entities.Count;
        _version++;
        _moves++;
    }

    internal void Reset()
    {
        _roots.Clear();
        _deadRoots = 0;
        _stale = false;
        _first = null;
        _last = null;
        _sorted = 0;
    }

    // Sorts the entities past `sorted` by label and merges them into the run before it. Each entity of
    // that run moves at most once, however many land below it.
    private void Merge(int sorted)
    {
        Span<Entity> held = CollectionsMarshal.AsSpan(_entities);
        Span<Entity> tail = held[sorted..];
        if (tail.Length > 1)
        {
            tail.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        }

        // The arrivals above the whole run stand where they belong already.
        int low = 0;
        if (sorted > 0)
        {
            long top = held[sorted - 1].Order;
            while (low < tail.Length && tail[low].Order < top)
            {
                low++;
            }
        }

        if (low > 0)
        {
            _arrived.Clear();
            _arrived.AddRange(tail[..low]);
            ReadOnlySpan<Entity> arrived = CollectionsMarshal.AsSpan(_arrived);

            // From the last arrival down. The run above an arrival moves up by the arrivals still to place,
            // in one block.
            int end = sorted;
            for (int arrival = low - 1; arrival >= 0; arrival--)
            {
                int slot = FirstAbove(held[..end], arrived[arrival].Order);
                held[slot..end].CopyTo(held[(slot + arrival + 1)..]);
                held[slot + arrival] = arrived[arrival];
                end = slot;
            }

            _arrived.Clear();
            sorted = end;
        }

        for (int index = sorted; index < held.Length; index++)
        {
            held[index].SceneSlot = index;
        }
    }

    // The index of the first entity of a run in label order whose label is above `order`.
    private static int FirstAbove(ReadOnlySpan<Entity> run, long order)
    {
        int low = 0;
        int high = run.Length;
        while (low < high)
        {
            int middle = (int)(((uint)low + (uint)high) >> 1);
            if (run[middle].Order < order)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    // Spreads the labels evenly again, once two neighbours leave no label between them or the last
    // nears the top. The order stands, and nothing sorted by label has to sort again.
    private void Respace()
    {
        if (_stale)
        {
            Rebuild();
            scene.Drawing.Invalidate();
            return;
        }

        long order = 0;
        for (Entity? entity = _first; entity is not null; entity = entity.OrderNext)
        {
            entity.Order = order += EndGap;
        }

        scene.Drawing.Respaced();
    }

    // Chains `entity` between two neighbours, either of them null at an end of the chain.
    private void Link(Entity? before, Entity entity, Entity? after)
    {
        entity.OrderPrev = before;
        entity.OrderNext = after;
        Link(before, entity);
        Link(entity, after);
    }

    // Makes `after` follow `before` in the chain, either of them null at an end of it.
    private void Link(Entity? before, Entity? after)
    {
        if (before is null)
        {
            _first = after;
        }
        else
        {
            before.OrderNext = after;
        }

        if (after is null)
        {
            _last = before;
        }
        else
        {
            after.OrderPrev = before;
        }
    }

    // The entity the chain holds right before where `entity` joins under its held parent.
    private Entity Before(Entity entity)
    {
        Entity parent = entity.Parent!;
        ReadOnlySpan<Entity> siblings = parent.Children;
        for (int slot = entity.ChildSlot - 1; slot >= 0; slot--)
        {
            if (Holds(siblings[slot]))
            {
                return LastHeldUnder(siblings[slot]);
            }
        }

        return parent;
    }

    // The last entity of a held entity's held subtree, itself when it holds no child.
    private Entity LastHeldUnder(Entity entity)
    {
        while (true)
        {
            ReadOnlySpan<Entity> children = entity.Children;
            int slot = children.Length - 1;
            while (slot >= 0 && !Holds(children[slot]))
            {
                slot--;
            }

            if (slot < 0)
            {
                return entity;
            }

            entity = children[slot];
        }
    }

    private bool Holds(Entity entity) => ReferenceEquals(entity.SceneOrNull, scene);

    private bool PlacedByParent(Entity entity) => entity.Parent is { } parent && Holds(parent);

    private void AddRoot(Entity entity)
    {
        if (_deadRoots * 2 > _roots.Count)
        {
            // An older entry of this entity is dead. The pass drops it, and the new one is its only entry.
            entity.Marked = true;
            CompactRoots();
            entity.Marked = false;
        }

        _roots.Add(entity);
    }

    // Keeps the entries that still name a root, once each, in order. Walked from the end so a root that
    // left and returned keeps its newest entry. Marked entities are the ones already kept.
    private void CompactRoots()
    {
        Span<Entity> roots = CollectionsMarshal.AsSpan(_roots);
        int first = roots.Length;
        for (int index = roots.Length - 1; index >= 0; index--)
        {
            Entity root = roots[index];
            if (Holds(root) && !PlacedByParent(root) && !root.Marked)
            {
                root.Marked = true;
                roots[--first] = root;
            }
        }

        int kept = roots.Length - first;
        for (int index = 0; index < kept; index++)
        {
            Entity root = roots[first + index];
            root.Marked = false;
            roots[index] = root;
        }

        _roots.RemoveRange(kept, roots.Length - kept);
        _deadRoots = 0;
    }

    // Iterative. A deep chain costs no stack.
    private void AppendSubtree(Entity root)
    {
        Entity entity = root;
        while (true)
        {
            entity.SceneSlot = _entities.Count;
            entity.Order = (_entities.Count + 1) * EndGap;
            Link(_last, entity, null);
            _entities.Add(entity);

            if (NextHeld(entity.Children, 0) is { } child)
            {
                entity = child;
                continue;
            }

            while (!ReferenceEquals(entity, root))
            {
                Entity parent = entity.Parent!;
                if (NextHeld(parent.Children, entity.ChildSlot + 1) is { } sibling)
                {
                    entity = sibling;
                    break;
                }

                entity = parent;
            }

            if (ReferenceEquals(entity, root))
            {
                return;
            }
        }
    }

    private Entity? NextHeld(ReadOnlySpan<Entity> children, int from)
    {
        for (int index = from; index < children.Length; index++)
        {
            if (Holds(children[index]))
            {
                return children[index];
            }
        }

        return null;
    }
}
