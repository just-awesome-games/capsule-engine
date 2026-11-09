using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

// A scene's draw pass and the cached draw order it walks. The pass refuses a change to the order while
// it runs, which keeps the order valid for the whole pass.
internal sealed class SceneDrawing
{
    // An entity's DrawPosition from landing at the end of the tree until the order next reads it.
    private const long AwaitingOrder = -1;

    // The most arrivals a read inserts one by one. Past this a rebuild costs less, whatever the order's size.
    private const int MostInserts = 256;

    private readonly Scene _scene;
    private readonly List<Renderer> _renderers = [];
    private readonly List<DrawKey> _keys = [];

    // The entities that landed at the end of the tree since the order was last read.
    private readonly List<Entity> _appended = [];

    // Above every key's position. A renderer appended to the tree follows every renderer in tree order.
    private long _nextPosition;

    // The lowest slot a leaving renderer nulled since the order was last read.
    private int? _firstVacated;
    private bool _renderersStale = true;
    private bool _drawing;

    // Whether renderers in one band order by their root's Y. A change invalidates the order.
    internal bool YSort
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            ThrowIfDrawing("changed YSort");
            field = value;
            _renderersStale = true;
        }
    }

    internal SceneDrawing(Scene scene)
    {
        _scene = scene;
    }

    // Rewrites view from the scene as it stands, calling every visible renderer's Draw in draw order.
    // A change to the scene from inside Draw throws. A frame is then the same however often it is built.
    // A host's camera, when one is passed, frames it in place of the scene's. runs, when passed, is
    // rewritten with the entity that stored each run of the frame's world sprites.
    internal void DrawFrame(FrameView view, CameraView? camera = null, List<DrawRun>? runs = null)
    {
        Scene scene = _scene;
        runs?.Clear();
        view.Clear();
        view.Camera = camera ?? scene.Camera.ToView();
        view.Canvas = scene.Run.Canvas;
        view.ClearColor = scene.ClearColor;
        view.Ambient = scene.Ambient;
        view.Sampling = scene.Sampling;
        view.ShaderGlobals = scene.ShaderGlobals;

        _drawing = true;
        try
        {
            ReadOnlySpan<Renderer> renderers = GetDrawOrder(scene.Entities);
            ClipRect? clip = null;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!renderer.Visible)
                {
                    continue;
                }

                // The only place the render space, draw order, scroll factor, tint, flash and material are
                // chosen.
                Entity entity = renderer.Entity!;
                if (entity.TryGetDrawStyle(out ColorRgba tint, out ColorRgba flash))
                {
                    view.Space = entity.Space;
                    view.SortsWithWorld = entity.InWorldOrder;
                    view.ScrollFactor = entity.ScrollFactor;
                    view.SetStyle(tint, flash);
                    view.Material = renderer.Material ?? entity.Material;

                    // A clip is per renderer state, and only screen space has one. A world renderer
                    // between two clipped ones leaves it alone, and a subtree computes its region once.
                    if (view.Space == RenderSpace.Screen && !ReferenceEquals(entity.NearestClip, clip))
                    {
                        clip = entity.NearestClip;
                        if (clip is null)
                        {
                            view.ClearClip();
                        }
                        else
                        {
                            view.BeginClip();
                            ClipRect.AddLevels(clip, view);
                            view.EndClip();
                        }
                    }

                    if (runs is not null && (runs.Count == 0 || !ReferenceEquals(runs[^1].Entity, entity)))
                    {
                        runs.Add(new DrawRun(entity, view.Sprites.Length, view.Polygons.Length));
                    }

                    renderer.Draw(view);
                }
            }
        }
        finally
        {
            view.Space = RenderSpace.World;
            view.SortsWithWorld = false;
            view.ScrollFactor = Vector2.One;
            view.SetStyle(ColorRgba.White, default);
            view.Material = null;
            view.ClearClip();
            _drawing = false;
        }
    }

    // Guards every change that reaches the draw order. The draw pass holds no deferral queue.
    internal void ThrowIfDrawing(string change)
    {
        if (_drawing)
        {
            throw new InvalidOperationException(
                $"A Renderer.Draw {change} in a {_scene.GetType().Name}. Draw only writes the FrameView it is handed. Make the change in a Step or LateStep.");
        }
    }

    private ReadOnlySpan<Renderer> GetDrawOrder(ReadOnlySpan<Entity> entities)
    {
        if (_renderersStale || _appended.Count > MostInserts)
        {
            RebuildRenderers(entities);
        }
        else if (YSort)
        {
            Resort();
        }
        else if (_firstVacated is not null || _appended.Count > 0)
        {
            Patch();
        }

        return CollectionsMarshal.AsSpan(_renderers);
    }

    // The next read rebuilds the whole order.
    internal void Invalidate() => _renderersStale = true;

    // An entity's band or renderers changed. One still waiting to enter the order is read as it enters.
    internal void Changed(Entity entity) => _renderersStale |= entity.DrawPosition != AwaitingOrder;

    // The entity landed at the end of the tree, and every entity already held kept its place in tree order.
    // A Y-sorted order rebuilds instead, because a key's Y moves and a search cannot find it.
    internal void Appended(Entity entity)
    {
        _renderersStale |= YSort;
        if (!_renderersStale)
        {
            entity.DrawPosition = AwaitingOrder;
            _appended.Add(entity);
        }
    }

    // The entity left, and every other entity kept its place in tree order. One still waiting holds no key to
    // find, and rebuilds.
    internal void Left(Entity entity)
    {
        long position = entity.DrawPosition;
        entity.DrawPosition = 0;
        _renderersStale |= YSort || position == AwaitingOrder;
        if (_renderersStale)
        {
            return;
        }

        Span<DrawKey> keys = CollectionsMarshal.AsSpan(_keys);
        foreach (Component component in entity.Components)
        {
            if (component is Renderer renderer)
            {
                int slot = keys.BinarySearch(new DrawKey(entity.DrawBand + renderer.ZIndex, position++));
                _renderers[slot] = null!;
                _firstVacated = Math.Min(_firstVacated ?? slot, slot);
            }
        }
    }

    // Sizes the order for renderers held at once and the entities that hold them. A read after pooled
    // entities attach then grows nothing.
    internal void Reserve(int renderers, int entities)
    {
        _renderers.EnsureCapacity(renderers);
        _keys.EnsureCapacity(renderers);
        _appended.EnsureCapacity(entities);
    }

    internal void Clear()
    {
        _renderers.Clear();
        _keys.Clear();
        _appended.Clear();
        _firstVacated = null;
        _nextPosition = 0;
        _renderersStale = false;
    }

    // The walk runs in tree order. Each entity's band is its parent's already-summed band plus its own
    // ZIndex.
    private void RebuildRenderers(ReadOnlySpan<Entity> entities)
    {
        _renderers.Clear();
        _keys.Clear();
        _appended.Clear();
        _firstVacated = null;

        bool ySort = YSort;
        bool banded = false;
        foreach (Entity entity in entities)
        {
            long band = entity.DrawBand = (entity.Parent?.DrawBand ?? 0) + entity.ZIndex;

            // A root only moves out of the scene, which rebuilds the order. The key can hold it.
            Entity? root = ySort && entity.Space == RenderSpace.World ? entity.Root : null;
            entity.DrawPosition = _renderers.Count;
            foreach (Component component in entity.Components)
            {
                if (component is Renderer renderer)
                {
                    long key = band + renderer.ZIndex;
                    banded |= key != 0;
                    _keys.Add(new DrawKey(key, _renderers.Count, root?.Position.Y ?? float.NegativeInfinity, root));
                    _renderers.Add(renderer);
                }
            }
        }

        _nextPosition = _renderers.Count;
        _renderersStale = false;

        // The walk yields entity order and then attachment order, which equal keys preserve. A scene
        // that bands nothing and sorts nothing by Y needs no sort.
        if (banded || ySort)
        {
            CollectionsMarshal.AsSpan(_keys).Sort(CollectionsMarshal.AsSpan(_renderers));
        }
    }

    // Takes out the entities that left and inserts those that arrived, with no comparison of the rest.
    private void Patch()
    {
        if (_firstVacated is { } first)
        {
            CloseVacated(first);
        }

        foreach (Entity entity in CollectionsMarshal.AsSpan(_appended))
        {
            entity.DrawPosition = _nextPosition;
            long band = entity.DrawBand = (entity.Parent?.DrawBand ?? 0) + entity.ZIndex;
            foreach (Component component in entity.Components)
            {
                if (component is Renderer renderer)
                {
                    DrawKey key = new(band + renderer.ZIndex, _nextPosition++);
                    int slot = ~CollectionsMarshal.AsSpan(_keys).BinarySearch(key);
                    _keys.Insert(slot, key);
                    _renderers.Insert(slot, renderer);
                }
            }
        }

        _appended.Clear();
    }

    private void CloseVacated(int first)
    {
        Span<DrawKey> keys = CollectionsMarshal.AsSpan(_keys);
        Span<Renderer> renderers = CollectionsMarshal.AsSpan(_renderers);
        int kept = first;
        for (int slot = first + 1; slot < renderers.Length; slot++)
        {
            if (renderers[slot] is { } renderer)
            {
                keys[kept] = keys[slot];
                renderers[kept++] = renderer;
            }
        }

        _keys.RemoveRange(kept, keys.Length - kept);
        _renderers.RemoveRange(kept, renderers.Length - kept);
        _firstVacated = null;
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

    // Each key carries its renderer's place in tree order beside the band and the root's Y. No two keys
    // compare equal, and the runtime's unstable sort produces the same order a stable sort would. Root is
    // null where Y takes no part: with Y-sorting off, and in screen space, whose Y stays below every root's.
    // A screen root sorted with the world then draws first in its band.
    private readonly record struct DrawKey(long Band, long Position, float Y = float.NegativeInfinity, Entity? Root = null)
        : IComparable<DrawKey>
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
