using System.Runtime.InteropServices;

namespace Capsule.Scenes;

// A scene's held entities in tree order: each root in the order it became one, then its subtree
// depth-first in parenting order. The order itself is a chain through the entities, each carrying a label
// that rises along it. An entity joins anywhere without moving another, a parent write moves its subtree
// as one run, and two labels compare two places in the order. The flat list is what a walk reads, and it
// catches up with the chain before it is read in order.
internal sealed class EntityTree(Scene scene)
{
    // How far past the last label an entity joining the end takes its own.
    private const long EndGap = 1L << 32;

    // The most of the gap an entity joining between two others takes, and it takes a sixteenth of a
    // gap narrower than sixteen of these. Children mostly join one after another ahead of the same
    // follower, and a small step leaves that gap room for many of them.
    private const long Step = 1L << 16;

    private readonly List<Entity> _entities = [];

    // Where a settle keeps the arrivals that land below the sorted run while it moves that run up.
    private readonly List<Entity> _arrived = [];

    // The entities a parent write moved along the chain whose slots in the list stand where they were.
    // The step walk reads the list, and they follow once it may change.
    private readonly List<Entity> _moved = [];

    private Entity? _first;
    private Entity? _last;

    // How many slots at the front of _entities stand in label order, holes among them. The rest joined
    // since the list last settled, in the order they joined.
    private int _sorted;

    // Null slots a detach left in _entities, and the lowest of them. The list settles before it is
    // next read in order. A batch of removals then costs one pass instead of one shift each.
    private int _holes;
    private int _firstHole;

    // Bumped whenever _entities changes. A walk that sees it change throws.
    private int _version;

    // Bumped whenever a held entity moves to another slot of _entities, which an entity joining moves
    // none to.
    private int _moves;

    // Bumped whenever a parent write gives held entities new labels out of their old order.
    private int _reorders;

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

    internal void Reserve(int entities)
    {
        _entities.EnsureCapacity(entities);
        _moved.EnsureCapacity(entities);
    }

    // Joins the chain after the held subtree of its nearest earlier held sibling, else right after its
    // parent. An entity no held parent places is a root and joins the end. No other entity moves.
    internal void Insert(Entity entity)
    {
        // The list counts as sorted only where no moved entity stands out of label order in it.
        Follow();

        // An entity joining the end of a settled list leaves it settled.
        if (Place(entity, entity, 1) && _sorted == _entities.Count)
        {
            _sorted++;
        }

        entity.SceneSlot = _entities.Count;
        _entities.Add(entity);
        _version++;
    }

    internal Entity Vacate(int index)
    {
        Entity entity = _entities[index];
        _entities[index] = null!;
        _firstHole = _holes++ == 0 ? index : Math.Min(_firstHole, index);

        Link(entity.OrderPrev, entity.OrderNext);
        entity.OrderPrev = null;
        entity.OrderNext = null;
        return entity;
    }

    // The entity's subtree, from it through `last`, is one run of the chain. The run moves to where the
    // links now place it, the end for an entity written to no parent, and takes labels there. No other
    // entity moves or changes its label unless the labels run out. Its slots in the list wait for Follow.
    internal void Reparented(Entity entity, Entity last)
    {
        int count = 0;
        for (Entity at = entity; ; at = at.OrderNext!)
        {
            count++;
            if (!at.AwaitsSlot)
            {
                at.AwaitsSlot = true;
                _moved.Add(at);
            }

            if (ReferenceEquals(at, last))
            {
                break;
            }
        }

        Link(entity.OrderPrev, last.OrderNext);
        Place(entity, last, count);
        _reorders++;
    }

    // Moves the slots of the entities a parent write moved past the sorted run, each leaving a hole, and
    // the next settle merges them in by label.
    internal void Follow()
    {
        if (_moved.Count == 0)
        {
            return;
        }

        foreach (Entity entity in CollectionsMarshal.AsSpan(_moved))
        {
            // One that left holds no slot, and one already past the sorted run merges by label anyway.
            entity.AwaitsSlot = false;
            int slot = entity.SceneSlot;
            if (!Holds(entity) || slot >= _sorted)
            {
                continue;
            }

            _entities[slot] = null!;
            _firstHole = _holes++ == 0 ? slot : Math.Min(_firstHole, slot);
            entity.SceneSlot = _entities.Count;
            _entities.Add(entity);
        }

        _moved.Clear();
        _version++;
        _moves++;
    }

    // Closes the holes detaches left and merges in the entities that joined since, which leaves the list
    // in tree order, and renumbers what moved.
    internal void Settle()
    {
        if (_holes == 0 && _sorted == _entities.Count)
        {
            return;
        }

        Follow();
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
        ForgetMoved();
        _first = null;
        _last = null;
        _sorted = 0;
    }

    private void ForgetMoved()
    {
        foreach (Entity entity in CollectionsMarshal.AsSpan(_moved))
        {
            entity.AwaitsSlot = false;
        }

        _moved.Clear();
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

    // Chains the run from `first` through `last`, `count` entities out of the chain, where the links place
    // `first`, and labels it there. Between two neighbours the run takes as little of the gap as an entity
    // joining there would. Returns whether the run joined the end.
    private bool Place(Entity first, Entity last, int count)
    {
        (Entity? before, Entity? after) = Neighbours(first);
        if (after is null ? before is not null && (long.MaxValue - before.Order) / EndGap < count : after.Order - before!.Order <= count)
        {
            Respace();
            (before, after) = Neighbours(first);
        }

        long step = after is null ? EndGap : Math.Min(Math.Max((after.Order - before!.Order) / count >> 4, 1), Step);
        long order = before?.Order ?? 0;
        for (Entity at = first; ; at = at.OrderNext!)
        {
            at.Order = order += step;
            if (ReferenceEquals(at, last))
            {
                break;
            }
        }

        Link(before, first);
        Link(last, after);
        return after is null;
    }

    // The entities the chain holds on either side of where `entity` joins: after its place under a held
    // parent, else at the end.
    private (Entity? Before, Entity? After) Neighbours(Entity entity)
    {
        Entity? before = PlacedByParent(entity) ? Before(entity) : _last;
        return (before, before is null ? _first : before.OrderNext);
    }

    // Spreads the labels evenly again, once two neighbours leave no label between them or the last
    // nears the top. The order stands, and nothing sorted by label has to sort again.
    private void Respace()
    {
        long order = 0;
        for (Entity? entity = _first; entity is not null; entity = entity.OrderNext)
        {
            entity.Order = order += EndGap;
        }

        scene.Drawing.Respaced();
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

    // The last entity of a held entity's held subtree, itself when it holds no child. The subtree is the
    // run of the chain from the entity to this one.
    internal Entity LastHeldUnder(Entity entity)
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
}
