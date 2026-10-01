using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tiles;

/// <summary>Makes the <see cref="TileMap"/> it is added to collide as its palette says.</summary>
/// <remarks>
/// It registers one grid of the map's cells with the scene's world while it is enabled and its map
/// is in a scene. Each cell collides on its tile type's layer, as its shape, one-way or solid-sided as
/// the palette entry says. It is not a <see cref="Collider2D"/>, because each cell takes its layer and
/// shape from the palette and the component has no single shape, offset or layer of its own. Bodies
/// report the tiles they touch, and the grid raises no contacts of its own. The map keeps a
/// <see cref="Entity.ScrollFactor"/> of one while the component is attached.
/// <para>
/// The grid is built from the map's current cells each time it registers. Disabling it, removing it or
/// taking the map out of the scene unregisters it at once, and a body touching the removed cells exits
/// them on its next settle. Re-enabling it registers a new grid, which collides as the map now draws.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// TileMap terrain = new(grid);
/// terrain.Add(new TileMapCollider2D());
/// </code>
/// </example>
public sealed class TileMapCollider2D : Component
{
    private bool _enabled = true;

    // The map this collider is attached to, and the scene's world while the map is in a scene.
    private TileMap? _map;
    private CollisionWorld2D? _world;

    // The grid's profile index for each palette entry and transform, at (palette * Count) + transform.
    // Only a shaped entry has a profile per transform. Null while no grid is registered.
    private int[]? _profiles;

    /// <summary>Whether the grid is in the scene's world, true by default.</summary>
    /// <remarks>A change in a scene adds or removes the grid at once.</remarks>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (_world is null)
            {
                return;
            }

            if (value)
            {
                Register();
            }
            else
            {
                Unregister();
            }
        }
    }

    // The grid in the scene's world, or null while the collider is disabled, unattached or its map is in
    // no scene.
    internal CollisionGrid2D? Grid { get; private set; }

    internal override TransformSupport Supports => TransformSupport.Position;

    internal override bool Steps => false;

    // Only a map whose palette names a layer can collide, and one grid covers the whole map.
    internal override void OnAttachedTo(Entity entity)
    {
        if (entity is not TileMap map)
        {
            throw new InvalidOperationException(
                $"A {nameof(TileMapCollider2D)} cannot be added to a {entity.GetType().Name}. Add it to a {nameof(TileMap)}, or give the entity a {nameof(Collider2D)}.");
        }

        if (map.Collider is not null)
        {
            throw new InvalidOperationException(
                $"This {nameof(TileMap)} already holds a {nameof(TileMapCollider2D)}. Keep one per map, since each registers the whole grid.");
        }

        if (!map.Grid.Collides)
        {
            throw new InvalidOperationException(
                $"This {nameof(TileMap)}'s palette names no collision layer, so a {nameof(TileMapCollider2D)} on it could never collide. Give a tile type a layer, or remove the collider.");
        }

        map.Collider = this;
        _map = map;
    }

    internal override void OnDetachingFrom(Entity entity)
    {
        _map!.Collider = null;
        _map = null;
    }

    // Forwards a painted cell to the registered grid. A collider with no grid reads the cell when it next
    // registers.
    internal void SetCell(int x, int y, int palette, TileTransform transform)
    {
        if (Grid is { } grid)
        {
            grid.SetCell(x, y, _profiles![(palette * TileTransforms.Count) + (int)transform]);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        _world = Entity!.Scene.Collision;
        if (_enabled)
        {
            Register();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        Unregister();
        _world = null;
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Width", Grid?.Width ?? 0);
        panel.Field("Height", Grid?.Height ?? 0);
        panel.Toggle("Enabled", _enabled, on => Enabled = on);
    }

    // Draws the grid's live edges on the Colliders channel, only for the cells the camera's view reaches
    // plus one cell of margin, which keeps a large grid cheap to draw. The derived state culls the shared
    // sides inside a solid run, and a wall shows as its outline. The map is anchored, so its edges carry
    // no motion.
    /// <inheritdoc/>
    protected internal override void OnDebugDraw()
    {
        if (Grid is not { } grid || Entity?.SceneOrNull is not { } scene)
        {
            return;
        }

        Rect view = scene.Camera.VisibleRegion;
        if (view.IsEmpty)
        {
            return;
        }

        int minX = Math.Max(CollisionGrid2D.FloorDiv(view.Left, grid.CellSize) - 1, 0);
        int minY = Math.Max(CollisionGrid2D.FloorDiv(view.Top, grid.CellSize) - 1, 0);
        int maxX = Math.Min(LastCell(view.Right, grid.CellSize) + 1, grid.Width - 1);
        int maxY = Math.Min(LastCell(view.Bottom, grid.CellSize) + 1, grid.Height - 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                CellState2D state = grid.StateAt(x, y);
                if ((state & CellState2D.Edges) != 0)
                {
                    DrawEdges(grid, x, y, state);
                    continue;
                }

                DrawFace(grid, x, y, state, CellState2D.FaceMinX);
                DrawFace(grid, x, y, state, CellState2D.FaceMaxX);
                DrawFace(grid, x, y, state, CellState2D.FaceMinY);
                DrawFace(grid, x, y, state, CellState2D.FaceMaxY);
            }
        }
    }

    // Returns the last cell a half-open rect's high edge reaches. That is the cell the edge lies in, or
    // the cell before it when the edge sits exactly on a boundary and so falls outside the rect.
    private static int LastCell(float edge, int cellSize)
    {
        int cell = CollisionGrid2D.FloorDiv(edge, cellSize);

        return cell * cellSize == edge ? cell - 1 : cell;
    }

    private static void DrawEdges(CollisionGrid2D grid, int x, int y, CellState2D state)
    {
        ReadOnlySpan<CellEdge2D> edges = grid.EdgesAt(x, y);
        Vector2 corner = grid.CellCorner(x, y);
        for (int index = 0; index < edges.Length; index++)
        {
            if ((state & CollisionGrid2D.EdgeBit(index)) != 0)
            {
                DebugDraw.Line(DebugDraw.Colliders, corner + edges[index].Start, corner + edges[index].End);
            }
        }
    }

    private static void DrawFace(CollisionGrid2D grid, int x, int y, CellState2D state, CellState2D face)
    {
        if ((state & face) != 0)
        {
            Aabb2D edge = grid.FaceEdge(x, y, face);
            DebugDraw.Line(DebugDraw.Colliders, edge.Min, edge.Max);
        }
    }

    // Builds the grid from the map's current cells. A collider re-enabled after SetTile collides as the
    // map now draws.
    private void Register()
    {
        TileMap map = _map!;
        CollisionWorld2D world = _world!;
        TileGrid tiles = map.Grid;
        ReadOnlySpan<TileType> palette = tiles.TileTypes;
        int shaped = 0;
        foreach (TileType tileType in palette)
        {
            shaped += tileType.Shape is null ? 0 : 1;
        }

        // The palette's own profiles come first, so an untransformed cell's profile index is its palette
        // index. Each shaped entry then appends one profile per other transform. Only the shape turns,
        // and a one-way tile still passes bodies from below.
        CellProfile2D[] profiles = new CellProfile2D[palette.Length + (shaped * (TileTransforms.Count - 1))];
        int[] lookup = new int[palette.Length * TileTransforms.Count];
        int next = palette.Length;
        for (int index = 0; index < palette.Length; index++)
        {
            TileType tileType = palette[index];
            CellProfile2D profile = new(
                tileType.Layer is { } layer ? world.Layer(layer) : null,
                tileType.Shape,
                tileType.OneWay,
                tileType.SolidSides);
            profiles[index] = profile;

            for (int transform = 0; transform < TileTransforms.Count; transform++)
            {
                int slot = (index * TileTransforms.Count) + transform;
                if (transform == 0 || tileType.Shape is not { } shape)
                {
                    lookup[slot] = index;
                    continue;
                }

                profiles[next] = profile with { Shape = TileTransforms.Apply(shape, tiles.TileSize, (TileTransform)transform) };
                lookup[slot] = next++;
            }
        }

        // The grid keeps its own profile indices. The map's cells stay palette indices.
        ReadOnlySpan<int> painted = map.Cells;
        ReadOnlySpan<TileTransform> facings = map.Transforms;
        int[] cells = new int[painted.Length];
        for (int index = 0; index < cells.Length; index++)
        {
            cells[index] = lookup[(painted[index] * TileTransforms.Count) + (int)facings[index]];
        }

        _profiles = lookup;
        Grid = world.AddGrid(tiles.TileSize, tiles.Width, tiles.Height, cells, profiles, this);
    }

    private void Unregister()
    {
        if (Grid is { } grid)
        {
            _world!.Remove(grid);
        }

        Grid = null;
        _profiles = null;
    }
}
