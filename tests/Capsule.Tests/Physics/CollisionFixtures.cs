using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Tests.Physics;

internal static class CollisionFixtures
{
    internal const int TileSize = 16;

    internal const string Solid = "solid";
    internal const string Platform = "platform";
    internal const string Crate = "crate";

    /// <summary>A second solid kind, letting a filter admit one wall and not the one beside it.</summary>
    internal const string Climb = "climb";

    /// <summary>The layer the single collidable cell of <see cref="OneWay"/> is on.</summary>
    internal const string Ledge = "ledge";

    /// <summary>
    /// What a move is allowed to land short of a surface by: the mover stops a slop clear of what
    /// blocked it, and a two-axis move can spend that twice.
    /// </summary>
    internal const float Tolerance = 2f * CollisionTolerance.LinearSlop;

    /// <summary>The cell below the diagonal from its bottom-left corner to its top-right, as a tile type's points.</summary>
    internal static readonly Vector2[] SlopeUpPoints = [new(0f, TileSize), new(TileSize, 0f), new(TileSize, TileSize)];

    /// <summary>The cell below the diagonal from its top-left corner to its bottom-right, as a tile type's points.</summary>
    internal static readonly Vector2[] SlopeDownPoints = [new(0f, 0f), new(TileSize, TileSize), new(0f, TileSize)];

    /// <summary>The cell below the diagonal from its bottom-left corner to its top-right.</summary>
    internal static readonly Shape2D SlopeUp = Shape2D.Polygon(SlopeUpPoints);

    /// <summary>The cell below the diagonal from its top-left corner to its bottom-right.</summary>
    internal static readonly Shape2D SlopeDown = Shape2D.Polygon(SlopeDownPoints);

    /// <summary>
    /// A grid painted from rows of characters: '.' empty, '#' solid, '-' one-way, '=' climbable, '/' a
    /// solid slope rising to the right and '\' one falling to the right, both at 45 degrees.
    /// </summary>
    internal static CollisionGrid2D Paint(CollisionWorld2D world, params string[] rows)
    {
        int width = rows[0].Length;
        int[] cells = new int[width * rows.Length];

        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cells[(y * width) + x] = rows[y][x] switch
                {
                    '#' => 1,
                    '-' => 2,
                    '=' => 3,
                    '/' => 4,
                    '\\' => 5,
                    _ => 0,
                };
            }
        }

        return world.AddGrid(TileSize, width, rows.Length, cells, Profiles(world));
    }

    /// <summary>Adds a grid whose cells are each a profile index, given row-major.</summary>
    internal static CollisionGrid2D AddGrid(
        this CollisionWorld2D world, int cellSize, int width, int height, int[] cells, ReadOnlySpan<CellProfile2D> profiles) =>
        world.AddGrid(cellSize, CellBlocks.FromDense(width, height, cells, default), profiles);

    /// <summary>Adds a grid whose cells are each a profile index. The grid reads cells in place.</summary>
    internal static CollisionGrid2D AddGrid(this CollisionWorld2D world, int cellSize, CellBlocks cells, ReadOnlySpan<CellProfile2D> profiles)
    {
        int[] identity = new int[profiles.Length];
        for (int index = 0; index < identity.Length; index++)
        {
            identity[index] = index;
        }

        return world.AddGrid(cellSize, cells, identity, 1, profiles);
    }

    internal static CellProfile2D[] Profiles(CollisionWorld2D world) =>
    [
        new(null),
        new(world.Layer(Solid)),
        new(world.Layer(Platform), OneWay: true),
        new(world.Layer(Climb)),
        new(world.Layer(Solid), SlopeUp),
        new(world.Layer(Solid), SlopeDown),
    ];

    /// <summary>
    /// Sends each collider far off and then back to where it stood, both passes in the order given. The
    /// broadphase rebuilds around them in that order.
    /// </summary>
    internal static void Rearrange(CollisionWorld2D world, ReadOnlySpan<ColliderHandle> handles, ReadOnlySpan<int> order)
    {
        Span<Vector2> home = stackalloc Vector2[handles.Length];
        for (int index = 0; index < handles.Length; index++)
        {
            home[index] = world.PositionOf(handles[index]);
        }

        foreach (int index in order)
        {
            world.SetPosition(handles[index], new Vector2(1_000f, 0f));
        }

        foreach (int index in order)
        {
            world.SetPosition(handles[index], home[index]);
        }
    }

    /// <summary>The indices below <paramref name="count"/> in the order numbered <paramref name="arrangement"/>, distinct for each arrangement below the count.</summary>
    internal static int[] Shuffled(int count, int arrangement)
    {
        // A stride coprime with the count visits every index once, and an offset coprime with it starts each arrangement apart.
        Assert.True(count % 5 != 0 && count % 3 != 0, $"{count} shares a factor with the stride or the offset.");
        int[] order = new int[count];
        for (int step = 0; step < count; step++)
        {
            order[step] = ((step * 5) + (arrangement * 3)) % count;
        }

        return order;
    }

    internal static Aabb2D Box(float x, float y, float width, float height) =>
        Aabb2D.FromCorner(new Vector2(x, y), new Vector2(width, height));

    /// <summary>
    /// A 3x3 grid whose only collidable cell is the middle one, a one-way tile. That cell spans
    /// x = 16..32 and y = 16..32, with its one edge along y = 16.
    /// </summary>
    internal static CollisionWorld2D OneWay()
    {
        CollisionWorld2D world = new();
        world.AddGrid(
            TileSize,
            3,
            3,
            [0, 0, 0, 0, 1, 0, 0, 0, 0],
            [new CellProfile2D(null), new CellProfile2D(world.Layer(Ledge), OneWay: true)]);

        return world;
    }
}

/// <summary>A bare collider with no body, which queries the world for itself and which nothing ever moves.</summary>
internal sealed class Prober : Entity
{
    internal BoxCollider2D Collider { get; }

    internal Prober(Vector2 position, params string[] detects)
        : this(position, new Vector2(8f, 8f), detects)
    {
    }

    internal Prober(Vector2 position, Vector2 size, params string[] detects)
        : base(position)
    {
        Collider = new BoxCollider2D(size);
        Collider.Detects = new(detects);
        Add(Collider);
    }
}

/// <summary>The rounded <see cref="Prober"/>: its sweeps take the iterated narrowphase.</summary>
internal sealed class RoundProber : Entity
{
    internal CircleCollider2D Collider { get; }

    internal RoundProber(Vector2 position, float radius, params string[] detects)
        : base(position)
    {
        Collider = new CircleCollider2D(radius);
        Collider.Detects = new(detects);
        Add(Collider);
    }
}

/// <summary>A box on a layer, 32 wide and 8 tall unless sized, that moves at its velocity each step.</summary>
internal sealed class Slab : Entity
{
    internal BoxCollider2D Collider { get; }

    internal Vector2 Velocity { get; set; }

    internal Slab(Vector2 position, string layer = CollisionFixtures.Platform, Vector2? size = null, bool oneWay = false, bool solidSides = false)
        : base(position)
    {
        Collider = new BoxCollider2D(size ?? new Vector2(32f, 8f)) { Layer = layer, OneWay = oneWay, SolidSides = solidSides };
        Add(Collider);
    }

    protected internal override void OnStep(in StepContext context)
    {
        if (Velocity != Vector2.Zero)
        {
            Position += Velocity * context.DeltaSeconds;
        }
    }
}

/// <summary>A body, 8 square unless sized, blocked by solid ground, platforms and crates and moved by the layers named.</summary>
internal sealed class Rider : Entity
{
    internal const float Edge = 8f;

    internal BoxCollider2D Collider { get; }

    internal KinematicBody2D Mover { get; }

    /// <summary>Whether each step moves the body down one unit.</summary>
    internal bool Falls { get; init; }

    /// <summary><see cref="KinematicBody2D.PlatformVelocity"/> as this step read it before the move.</summary>
    internal Vector2 Read { get; private set; }

    internal Rider(Vector2 position, params string[] movedBy)
        : this(position, new Vector2(Edge, Edge), movedBy)
    {
    }

    internal Rider(Vector2 position, Vector2 size, params string[] movedBy)
        : base(position)
    {
        Collider = new BoxCollider2D(size);
        Add(Collider);
        Mover = new KinematicBody2D(Collider)
        {
            BlockedBy = new(CollisionFixtures.Solid, CollisionFixtures.Platform, CollisionFixtures.Crate),
            MovedBy = new(movedBy),
        };
        Add(Mover);
    }

    protected internal override void OnStep(in StepContext context)
    {
        Read = Mover.PlatformVelocity;
        if (Falls)
        {
            Mover.Move(new Vector2(0f, 1f));
        }
    }
}

/// <summary>A static box or convex polygon collider, on the layer "solid" unless another is named.</summary>
internal sealed class Block : Entity
{
    internal Block(Vector2 position, Vector2 size, string layer = CollisionFixtures.Solid, bool oneWay = false)
        : base(position) =>
        Add(new BoxCollider2D(size) { Layer = layer, OneWay = oneWay });

    internal Block(Vector2[] points, string layer = CollisionFixtures.Solid, bool oneWay = false)
        : base(Vector2.Zero) =>
        Add(new PolygonCollider2D(points) { Layer = layer, OneWay = oneWay });
}
