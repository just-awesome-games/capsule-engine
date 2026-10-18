using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

// A scene's draw pass and the cached draw order it walks. The pass refuses a change to the order while
// it runs, which keeps the order valid for the whole pass.
internal sealed class SceneDrawing
{
    private readonly Scene _scene;
    private readonly List<Renderer> _renderers = [];
    private readonly List<DrawKey> _keys = [];
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

                // The only place the render space, scroll factor, tint, flash and material are chosen.
                Entity entity = renderer.Entity!;
                if (entity.TryGetDrawStyle(out ColorRgba tint, out ColorRgba flash))
                {
                    view.Space = entity.Space;
                    view.ScrollFactor = entity.ScrollFactor;
                    view.SetStyle(tint, flash);
                    view.Material = renderer.Material;

                    // A clip is per renderer state, and only the screen layer has one. A world renderer
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
                        runs.Add(new DrawRun(entity, view.Sprites.Length));
                    }

                    renderer.Draw(view);
                }
            }
        }
        finally
        {
            view.Space = RenderSpace.World;
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
        else if (YSort)
        {
            Resort();
        }

        return CollectionsMarshal.AsSpan(_renderers);
    }

    internal void Invalidate() => _renderersStale = true;

    // Sizes the order for renderers held at once. A rebuild after pooled entities attach then grows nothing.
    internal void Reserve(int renderers)
    {
        _renderers.EnsureCapacity(renderers);
        _keys.EnsureCapacity(renderers);
    }

    internal void Clear()
    {
        _renderers.Clear();
        _keys.Clear();
        _renderersStale = false;
    }

    // The walk runs in tree order. Each entity's band is its parent's already-summed band plus its own
    // ZIndex.
    private void RebuildRenderers(ReadOnlySpan<Entity> entities)
    {
        _renderers.Clear();
        _keys.Clear();

        bool ySort = YSort;
        bool banded = false;
        foreach (Entity entity in entities)
        {
            long band = entity.DrawBand = (entity.Parent?.DrawBand ?? 0) + entity.ZIndex;

            // A root only moves out of the scene, which rebuilds the order. The key can hold it.
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

    // Each key carries its renderer's walk position beside the band and the root's Y. No two keys compare
    // equal, and the runtime's unstable sort produces the same order a stable sort would. Root is
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
