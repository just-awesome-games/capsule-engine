using System.Runtime.InteropServices;
using Capsule.Scenes;

namespace Capsule.Rendering;

// Holds the cached draw order. A change made during a draw takes effect on the next frame.
internal sealed class SceneRenderIndex
{
    private readonly List<Renderer> _renderers = [];
    private readonly List<DrawKey> _keys = [];
    private bool _renderersStale = true;
    private bool _rebuildDeferred;

    // ySort: whether renderers in one band order by their root's Y. The scene invalidates the index
    // when it changes, so every call between two rebuilds passes the same value.
    internal ReadOnlySpan<Renderer> GetDrawOrder(ReadOnlySpan<Entity> entities, bool ySort)
    {
        if (_renderersStale)
        {
            RebuildRenderers(entities, ySort);
        }
        else if (ySort)
        {
            Resort();
        }

        return CollectionsMarshal.AsSpan(_renderers);
    }

    internal void Invalidate(bool drawing)
    {
        if (drawing)
        {
            _rebuildDeferred = true;
        }
        else
        {
            _renderersStale = true;
        }
    }

    internal void EndDraw()
    {
        _renderersStale |= _rebuildDeferred;
        _rebuildDeferred = false;
    }

    internal void Clear()
    {
        _renderers.Clear();
        _keys.Clear();
        _renderersStale = false;
        _rebuildDeferred = false;
    }

    // The walk runs in tree order, so each entity's band is its parent's already-summed band plus its own
    // ZIndex.
    private void RebuildRenderers(ReadOnlySpan<Entity> entities, bool ySort)
    {
        _renderers.Clear();
        _keys.Clear();

        bool banded = false;
        foreach (Entity entity in entities)
        {
            long band = entity.DrawBand = (entity.Parent?.DrawBand ?? 0) + entity.ZIndex;

            // A root only moves out of the scene, which rebuilds the index, so the key can hold it.
            Entity? root = ySort && entity.Space == RenderSpace.World ? entity.Root : null;
            foreach (Component component in entity.Components)
            {
                if (component is Renderer renderer)
                {
                    long key = band + renderer.ZIndex;
                    banded |= key != 0;
                    _keys.Add(new DrawKey(key, root?.Position.Y ?? 0f, _renderers.Count, root));
                    _renderers.Add(renderer);
                }
            }
        }

        _renderersStale = false;

        // The walk yields entity order and then attachment order, which equal keys preserve. A scene
        // that bands nothing and sorts nothing by Y needs no sort.
        if (banded || ySort)
        {
            CollectionsMarshal.AsSpan(_keys).Sort(CollectionsMarshal.AsSpan(_renderers));
        }
    }

    // Refreshes each key's Y and re-sorts the order the last frame kept. Motion barely reorders between
    // frames, and an insertion sort is linear on input that is nearly sorted.
    private void Resort()
    {
        Span<DrawKey> keys = CollectionsMarshal.AsSpan(_keys);
        Span<Renderer> renderers = CollectionsMarshal.AsSpan(_renderers);

        for (int index = 0; index < keys.Length; index++)
        {
            if (keys[index].Root is { } root)
            {
                keys[index] = keys[index] with { Y = root.Position.Y };
            }
        }

        for (int index = 1; index < keys.Length; index++)
        {
            DrawKey key = keys[index];
            if (keys[index - 1].CompareTo(key) <= 0)
            {
                continue;
            }

            Renderer renderer = renderers[index];
            int slot = index;
            do
            {
                keys[slot] = keys[slot - 1];
                renderers[slot] = renderers[slot - 1];
                slot--;
            }
            while (slot > 0 && keys[slot - 1].CompareTo(key) > 0);

            keys[slot] = key;
            renderers[slot] = renderer;
        }
    }

    // Each key carries its renderer's walk position beside the band and the root's Y, so no two keys
    // compare equal and the runtime's unstable sort produces the same order a stable sort would. Root is
    // null where Y takes no part: with Y-sorting off, and on the screen layer, whose Y stays zero.
    private readonly record struct DrawKey(long Band, float Y, int Position, Entity? Root) : IComparable<DrawKey>
    {
        public int CompareTo(DrawKey other)
        {
            int band = Band.CompareTo(other.Band);
            if (band != 0)
            {
                return band;
            }

            int y = Y.CompareTo(other.Y);

            return y != 0 ? y : Position.CompareTo(other.Position);
        }
    }
}
