using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

// A scene's draw pass and the cached draw order it walks. The pass refuses a change to the order while
// it runs, which keeps the order valid for the whole pass.
internal sealed class SceneDrawing
{
    // The widest span of bands a rebuild groups by counting. A wider one sorts by comparison.
    private const int MostCountedBands = 4096;

    private readonly Scene _scene;
    private List<Renderer> _renderers = [];
    private List<DrawKey> _keys = [];

    // Where a rebuild groups the order by band, and where a patch sorts its arrivals. Empty between reads.
    private List<Renderer> _spareRenderers = [];
    private List<DrawKey> _spareKeys = [];

    // How many keys a rebuild found in each band, then where each band starts.
    private int[] _bandStarts = [];

    // The entities that joined the tree since the order was last read. One that left again no longer
    // awaits the order, and one that left and came back is here twice.
    private readonly List<Entity> _arrived = [];

    // The lowest slot a leaving renderer nulled since the order was last read.
    private int? _firstVacated;

    // An entity left a Y-sorted order since it was last read. Its keys stand wherever their Y last put
    // them, and the next read looks for them.
    private bool _leftUnfound;
    private bool _renderersStale = true;
    private bool _drawing;

    // How many times the whole order was worked out from the tree, which a patch avoids.
    internal int Rebuilds { get; private set; }

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
        if (_renderersStale)
        {
            RebuildRenderers(entities);
        }
        else
        {
            if (YSort)
            {
                DropDeparted();
                Resort();
            }

            if (_firstVacated is not null || _arrived.Count > 0)
            {
                Patch();
            }
        }

        return CollectionsMarshal.AsSpan(_renderers);
    }

    // The next read rebuilds the whole order.
    internal void Invalidate() => _renderersStale = true;

    // An entity's band or renderers are about to change. Its keys leave as they stand, and it enters the
    // order again as an arrival, read as it then stands. One still waiting to enter is read as it
    // enters anyway. A band also moves every descendant's, and the order rebuilds unless they all wait
    // to enter it too.
    internal void Changing(Entity entity, bool band)
    {
        if (_renderersStale)
        {
            return;
        }

        if (band && HoldsKeysBelow(entity))
        {
            _renderersStale = true;
            return;
        }

        if (entity.AwaitsDrawOrder)
        {
            return;
        }

        if (YSort)
        {
            _renderersStale = true;
            return;
        }

        Left(entity);
        entity.AwaitsDrawOrder = true;
        _arrived.Add(entity);
    }

    // The entity joined the tree, and every entity already held kept its label.
    internal void Arrived(Entity entity)
    {
        if (!_renderersStale)
        {
            entity.AwaitsDrawOrder = true;
            _arrived.Add(entity);
        }
    }

    // The entity left, and every other entity kept its label. One still waiting holds no key to take out.
    // In a Y-sorted order a key's Y has moved since it was placed and a search cannot find it, and the
    // next read drops the keys of whatever left.
    internal void Left(Entity entity)
    {
        bool awaited = entity.AwaitsDrawOrder;
        entity.AwaitsDrawOrder = false;
        if (_renderersStale)
        {
            return;
        }

        if (YSort)
        {
            _leftUnfound = true;
            return;
        }

        if (awaited)
        {
            return;
        }

        Span<DrawKey> keys = CollectionsMarshal.AsSpan(_keys);
        int ordinal = 0;
        foreach (Component component in entity.Components)
        {
            if (component is Renderer renderer)
            {
                int slot = keys.BinarySearch(new DrawKey(entity.DrawBand + renderer.ZIndex, entity.Order, ordinal++));
                _renderers[slot] = null!;
                _firstVacated = Math.Min(_firstVacated ?? slot, slot);
            }
        }
    }

    // Whether a descendant of the entity may hold a key in the order: a child that waits for no read of
    // it, or one with children of its own, which this does not look under.
    private bool HoldsKeysBelow(Entity entity)
    {
        foreach (Entity child in entity.Children)
        {
            if (ReferenceEquals(child.SceneOrNull, _scene) && (!child.AwaitsDrawOrder || child.Children.Length > 0))
            {
                return true;
            }
        }

        return false;
    }

    // The tree spread its labels again and kept its order. Each key takes its entity's new label where
    // it stands.
    internal void Respaced()
    {
        if (_renderersStale)
        {
            return;
        }

        DropDeparted();
        if (_firstVacated is { } first)
        {
            CloseVacated(first);
        }

        Span<DrawKey> keys = CollectionsMarshal.AsSpan(_keys);
        ReadOnlySpan<Renderer> renderers = CollectionsMarshal.AsSpan(_renderers);
        for (int index = 0; index < keys.Length; index++)
        {
            keys[index] = keys[index] with { Order = renderers[index].Entity!.Order };
        }
    }

    // Sizes the order for renderers held at once and the entities that hold them. A read after pooled
    // entities attach then grows nothing.
    internal void Reserve(int renderers, int entities)
    {
        _renderers.EnsureCapacity(renderers);
        _keys.EnsureCapacity(renderers);
        _arrived.EnsureCapacity(entities);
    }

    internal void Clear()
    {
        _renderers.Clear();
        _keys.Clear();
        _arrived.Clear();
        _firstVacated = null;
        _leftUnfound = false;
        _renderersStale = false;
    }

    // The walk runs in tree order. Each entity's band is its parent's already-summed band plus its own
    // ZIndex, or its own ZIndex alone where that is absolute.
    private void RebuildRenderers(ReadOnlySpan<Entity> entities)
    {
        _renderers.Clear();
        _keys.Clear();
        _arrived.Clear();
        _firstVacated = null;
        _leftUnfound = false;
        Rebuilds++;

        bool ySort = YSort;
        long lowest = 0;
        long highest = 0;
        foreach (Entity entity in entities)
        {
            long band = entity.SumDrawBand();

            // An entity takes another root only by a parent write, which rebuilds the order. The key can hold it.
            Entity? root = ySort && entity.Space == RenderSpace.World ? entity.Root : null;
            entity.AwaitsDrawOrder = false;
            int ordinal = 0;
            foreach (Component component in entity.Components)
            {
                if (component is Renderer renderer)
                {
                    long key = band + renderer.ZIndex;
                    lowest = Math.Min(lowest, key);
                    highest = Math.Max(highest, key);
                    _keys.Add(new DrawKey(key, entity.Order, ordinal++, root?.Position.Y ?? float.NegativeInfinity, root));
                    _renderers.Add(renderer);
                }
            }
        }

        _renderersStale = false;

        // The walk yields entity order and then attachment order, which equal keys preserve. A scene
        // that bands nothing and sorts nothing by Y needs no sort.
        if (ySort)
        {
            CollectionsMarshal.AsSpan(_keys).Sort(CollectionsMarshal.AsSpan(_renderers));
        }
        else if (lowest != highest)
        {
            GroupByBand(lowest, highest);
        }
    }

    // Sorts an order that stands in tree order by band. Keeping the keys of each band in the order they
    // stand is the whole sort, and counting them does that without comparing two keys.
    private void GroupByBand(long lowest, long highest)
    {
        Span<DrawKey> keys = CollectionsMarshal.AsSpan(_keys);
        Span<Renderer> renderers = CollectionsMarshal.AsSpan(_renderers);
        if ((ulong)(highest - lowest) >= MostCountedBands)
        {
            keys.Sort(renderers);
            return;
        }

        int bands = (int)(highest - lowest) + 1;
        if (_bandStarts.Length < bands)
        {
            _bandStarts = new int[bands];
        }

        Span<int> starts = _bandStarts.AsSpan(0, bands);
        starts.Clear();
        foreach (ref readonly DrawKey key in keys)
        {
            starts[(int)(key.Band - lowest)]++;
        }

        int start = 0;
        foreach (ref int band in starts)
        {
            (band, start) = (start, start + band);
        }

        _spareKeys.EnsureCapacity(_keys.Capacity);
        _spareRenderers.EnsureCapacity(_renderers.Capacity);
        CollectionsMarshal.SetCount(_spareKeys, keys.Length);
        CollectionsMarshal.SetCount(_spareRenderers, keys.Length);
        Span<DrawKey> groupedKeys = CollectionsMarshal.AsSpan(_spareKeys);
        Span<Renderer> groupedRenderers = CollectionsMarshal.AsSpan(_spareRenderers);
        for (int index = 0; index < keys.Length; index++)
        {
            int slot = starts[(int)(keys[index].Band - lowest)]++;
            groupedKeys[slot] = keys[index];
            groupedRenderers[slot] = renderers[index];
        }

        (_keys, _spareKeys) = (_spareKeys, _keys);
        (_renderers, _spareRenderers) = (_spareRenderers, _renderers);
        _spareKeys.Clear();
        _spareRenderers.Clear();
    }

    // Takes out the entities that left and merges in those that arrived, with no comparison among the
    // rest. Each renderer held moves at most once, however many arrive below it.
    private void Patch()
    {
        if (_firstVacated is { } first)
        {
            CloseVacated(first);
        }

        bool inOrder = true;
        bool ySort = YSort;
        foreach (Entity entity in CollectionsMarshal.AsSpan(_arrived))
        {
            if (!entity.AwaitsDrawOrder || !ReferenceEquals(entity.SceneOrNull, _scene))
            {
                continue;
            }

            // A parent that arrived too may stand later in the list, its own band not yet summed.
            entity.AwaitsDrawOrder = false;
            long band = entity.SumDrawBandFromAncestors();
            Entity? root = ySort && entity.Space == RenderSpace.World ? entity.Root : null;
            int ordinal = 0;
            foreach (Component component in entity.Components)
            {
                if (component is Renderer renderer)
                {
                    DrawKey key = new(band + renderer.ZIndex, entity.Order, ordinal++, root?.Position.Y ?? float.NegativeInfinity, root);
                    inOrder &= _spareKeys.Count == 0 || _spareKeys[^1].CompareTo(key) < 0;
                    _spareKeys.Add(key);
                    _spareRenderers.Add(renderer);
                }
            }
        }

        _arrived.Clear();

        Span<DrawKey> arrivedKeys = CollectionsMarshal.AsSpan(_spareKeys);
        Span<Renderer> arrivedRenderers = CollectionsMarshal.AsSpan(_spareRenderers);
        if (!inOrder)
        {
            // A sort given no comparison allocates on every call in a NativeAOT build.
            arrivedKeys.Sort(arrivedRenderers, static (left, right) => left.CompareTo(right));
        }

        int held = _keys.Count;
        CollectionsMarshal.SetCount(_keys, held + arrivedKeys.Length);
        CollectionsMarshal.SetCount(_renderers, held + arrivedKeys.Length);
        Span<DrawKey> keys = CollectionsMarshal.AsSpan(_keys);
        Span<Renderer> renderers = CollectionsMarshal.AsSpan(_renderers);

        // From the last arrival down. The held keys above an arrival move up by the arrivals still to
        // place, in one block.
        for (int arrival = arrivedKeys.Length - 1; arrival >= 0; arrival--)
        {
            int slot = ~keys[..held].BinarySearch(arrivedKeys[arrival]);
            keys[slot..held].CopyTo(keys[(slot + arrival + 1)..]);
            renderers[slot..held].CopyTo(renderers[(slot + arrival + 1)..]);
            keys[slot + arrival] = arrivedKeys[arrival];
            renderers[slot + arrival] = arrivedRenderers[arrival];
            held = slot;
        }

        _spareKeys.Clear();
        _spareRenderers.Clear();
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

    // Takes out the keys of the entities that left a Y-sorted order, which hold their renderers still.
    // One that left and came back awaits the order again, and its old keys go too.
    private void DropDeparted()
    {
        if (!_leftUnfound)
        {
            return;
        }

        _leftUnfound = false;
        Span<Renderer> renderers = CollectionsMarshal.AsSpan(_renderers);
        for (int slot = 0; slot < renderers.Length; slot++)
        {
            if (renderers[slot].Entity is not { AwaitsDrawOrder: false } entity || !ReferenceEquals(entity.SceneOrNull, _scene))
            {
                renderers[slot] = null!;
                _firstVacated ??= slot;
            }
        }

        if (_firstVacated is { } first)
        {
            CloseVacated(first);
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

    // Each key carries its entity's label in tree order and its renderer's place among that entity's,
    // beside the band and the root's Y. No two keys compare equal, and the runtime's unstable sort
    // produces the same order a stable sort would. Root is null where Y takes no part: with Y-sorting off,
    // and in screen space, whose Y stays below every root's. A screen root sorted with the world then
    // draws first in its band.
    private readonly record struct DrawKey(long Band, long Order, int Ordinal, float Y = float.NegativeInfinity, Entity? Root = null)
        : IComparable<DrawKey>
    {
        // The re-sort of a Y-sorted order compares in its inner loop, where a call costs more than the comparison.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(DrawKey other)
        {
            int band = Band.CompareTo(other.Band);
            if (band != 0)
            {
                return band;
            }

            int y = Y.CompareTo(other.Y);

            if (y != 0)
            {
                return y;
            }

            int order = Order.CompareTo(other.Order);

            return order != 0 ? order : Ordinal.CompareTo(other.Ordinal);
        }
    }
}
