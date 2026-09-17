using System.Runtime.InteropServices;
using Capsule.Scenes;

namespace Capsule.Rendering;

// Owns cached draw order; changes during a draw take effect on the next frame.
internal sealed class SceneRenderIndex
{
    private readonly List<Renderer> _renderers = [];
    private readonly List<DrawKey> _keys = [];
    private bool _renderersStale = true;
    private bool _rebuildDeferred;

    internal ReadOnlySpan<Renderer> GetDrawOrder(ReadOnlySpan<Entity> entities)
    {
        if (_renderersStale)
        {
            RebuildRenderers(entities);
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
        _renderersStale = false;
        _rebuildDeferred = false;
    }

    // The walk is tree order, so each entity's band — its ZIndex summed up the ancestry — is
    // the parent's already summed plus its own.
    private void RebuildRenderers(ReadOnlySpan<Entity> entities)
    {
        _renderers.Clear();
        _keys.Clear();

        bool banded = false;
        foreach (Entity entity in entities)
        {
            long band = entity.DrawBand = (entity.Parent?.DrawBand ?? 0) + entity.ZIndex;
            foreach (Component component in entity.Components)
            {
                if (component is Renderer renderer)
                {
                    long key = band + renderer.ZIndex;
                    banded |= key != 0;
                    _keys.Add(new DrawKey(key, _renderers.Count));
                    _renderers.Add(renderer);
                }
            }
        }

        _renderersStale = false;

        // The walk yields entity order and then attachment order, which is exactly what an equal
        // key keeps, so a scene that bands nothing is already in draw order.
        if (banded)
        {
            CollectionsMarshal.AsSpan(_keys).Sort(CollectionsMarshal.AsSpan(_renderers));
        }
    }

    // Each key carries its renderer's walk position beside the band, so no two keys are equal and
    // the runtime's unstable sort lands where a stable one would.
    private readonly record struct DrawKey(long Band, int Position) : IComparable<DrawKey>
    {
        public int CompareTo(DrawKey other)
        {
            int band = Band.CompareTo(other.Band);

            return band != 0 ? band : Position.CompareTo(other.Position);
        }
    }
}
