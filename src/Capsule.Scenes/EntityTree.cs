using System.Runtime.InteropServices;

namespace Capsule.Scenes;

// A scene's held entities flattened in tree order: each root in insertion order, then its subtree
// depth-first in parenting order. The links live on Entity, and this keeps the flat list and the
// ordered roots agreeing with them.
internal sealed class EntityTree(Scene scene)
{
    private readonly List<Entity> _entities = [];
    private readonly List<Entity> _roots = [];

    // Null slots a detach left in _entities, and the lowest of them. The list compacts before it is
    // next read in order. A batch of removals then costs one pass instead of one shift each.
    private int _holes;
    private int _firstHole;

    // Entries in _roots that no longer name a root, or name one a newer entry also names. A leaving root
    // costs nothing but this count. They are dropped once they pass half the list, so a root joining or
    // leaving costs O(1) amortized.
    private int _deadRoots;

    // Bumped whenever an entity moves to another slot of _entities. A walk that sees it change throws.
    private int _version;

    // A parent write moved something and the list no longer follows the links.
    private bool _stale;

    // Every slot, null where a detach left a hole. A reader that needs tree order calls Compact first.
    internal List<Entity> Slots => _entities;

    internal ReadOnlySpan<Entity> Held
    {
        get
        {
            Compact();
            return CollectionsMarshal.AsSpan(_entities);
        }
    }

    internal int Version => _version;

    internal bool Stale => _stale;

    internal void Reserve(int entities)
    {
        _entities.EnsureCapacity(entities);

        // Dead entries make the root list up to twice the live roots before it compacts.
        _roots.EnsureCapacity(entities * 2);
    }

    // Lands after the held subtree of its nearest earlier held sibling, else right after its parent.
    // An entity no held parent places is a root and joins the end of the roots.
    internal void Insert(Entity entity)
    {
        // A subtree's span is contiguous only once the holes are gone.
        Compact();
        int index = _entities.Count;
        bool placed = PlacedByParent(entity);
        if (placed)
        {
            Entity parent = entity.Parent!;
            index = parent.SceneSlot + 1;
            ReadOnlySpan<Entity> siblings = parent.Children;
            for (int slot = entity.ChildSlot - 1; slot >= 0; slot--)
            {
                if (Holds(siblings[slot]))
                {
                    index = siblings[slot].SceneSlot + HeldCount(siblings[slot]);
                    break;
                }
            }
        }

        _entities.Insert(index, entity);
        _version++;
        Renumber(index);

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

        if (!PlacedByParent(entity))
        {
            _deadRoots++;
        }

        return entity;
    }

    // A held root written to a parent leaves the roots, and a held entity written to none joins their end.
    // Either way the flat list no longer follows the links.
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

    // Refills the list in tree order from the roots and the links.
    internal void Rebuild()
    {
        CompactRoots();
        _entities.Clear();
        foreach (Entity root in CollectionsMarshal.AsSpan(_roots))
        {
            AppendSubtree(root);
        }

        _holes = 0;
        _stale = false;
        _version++;
    }

    // Closes the holes detaches left, keeping tree order, and renumbers what moved.
    internal void Compact()
    {
        if (_holes == 0)
        {
            return;
        }

        Span<Entity> held = CollectionsMarshal.AsSpan(_entities);
        int kept = _firstHole;
        for (int index = kept + 1; index < held.Length; index++)
        {
            Entity entity = held[index];
            if (entity is not null)
            {
                entity.SceneSlot = kept;
                held[kept++] = entity;
            }
        }

        _entities.RemoveRange(kept, _entities.Count - kept);
        _holes = 0;
        _version++;

        if (_deadRoots * 2 > _roots.Count)
        {
            CompactRoots();
        }
    }

    internal void Reset()
    {
        _roots.Clear();
        _deadRoots = 0;
        _stale = false;
    }

    private bool Holds(Entity entity) => ReferenceEquals(entity.SceneOrNull, scene);

    private bool PlacedByParent(Entity entity) => entity.Parent is { } parent && Holds(parent);

    private void AddRoot(Entity entity)
    {
        if (_deadRoots * 2 > _roots.Count)
        {
            // An older entry of this entity is dead, so the pass drops it and the new one is its only entry.
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

    // Restores each entity's slot from `from` on, after an insert shifted them.
    private void Renumber(int from)
    {
        for (int index = from; index < _entities.Count; index++)
        {
            _entities[index].SceneSlot = index;
        }
    }

    private int HeldCount(Entity entity)
    {
        int count = 1;
        foreach (Entity child in entity.Children)
        {
            if (Holds(child))
            {
                count += HeldCount(child);
            }
        }

        return count;
    }

    // Iterative, so a deep chain costs no stack.
    private void AppendSubtree(Entity root)
    {
        Entity entity = root;
        while (true)
        {
            entity.SceneSlot = _entities.Count;
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
