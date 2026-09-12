using System.Runtime.InteropServices;

namespace Capsule.Scenes.Rendering;

// Owns cached draw order; changes during a draw take effect on the next frame.
internal sealed class SceneRenderIndex
{
    private readonly List<Renderer> _renderers = [];
    private long[] _rendererKeys = [];
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

    private void RebuildRenderers(ReadOnlySpan<Entity> entities)
    {
        _renderers.Clear();

        bool banded = false;
        foreach (Entity entity in entities)
        {
            long band = entity.ZIndex;
            foreach (Component component in entity.Components)
            {
                if (component is Renderer renderer)
                {
                    banded |= band + renderer.ZIndex != 0;
                    _renderers.Add(renderer);
                }
            }
        }

        _renderersStale = false;

        // The walk yields entity order and then attachment order, which is exactly what an equal
        // key keeps, so a scene that bands nothing is already in draw order.
        if (banded)
        {
            SortRenderers();
        }
    }

    // Each key carries its renderer's walk position in its low bits, so no two keys are equal and
    // the runtime's unstable sort lands where a stable one would. The widened sum of two ints
    // spans exactly 33 signed bits, which leaves 31 for the position: a scene of 2^31 or more
    // renderers would collide two of them and lose the tie-break.
    private void SortRenderers()
    {
        int count = _renderers.Count;
        if (_rendererKeys.Length < count)
        {
            Array.Resize(ref _rendererKeys, Math.Max(count, _rendererKeys.Length * 2));
        }

        Span<Renderer> renderers = CollectionsMarshal.AsSpan(_renderers);
        Span<long> keys = _rendererKeys.AsSpan(0, count);
        for (int index = 0; index < count; index++)
        {
            Renderer renderer = renderers[index];
            keys[index] = (EffectiveKey(renderer) << 31) | (long)index;
        }

        keys.Sort(renderers);
    }

    // Widened before the addition: two ints at the far end of their range sum past what an int
    // holds, and a wrapped key would sort a foreground band under a background one.
    private static long EffectiveKey(Renderer renderer) =>
        (long)renderer.Entity!.ZIndex + renderer.ZIndex;
}
