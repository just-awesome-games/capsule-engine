using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Physics;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Tiles;

public sealed class TileTransformTests
{
    private const int Size = 16;

    // A transformed shape collides exactly as the same polygon authored that way: the same edges,
    // normals and covered sides. FlipX turns the rising slope into the falling one.
    [Theory]
    [InlineData(TileTransform.FlipX, new float[] { 0, 0, 16, 16, 0, 16 })]
    [InlineData(TileTransform.FlipY, new float[] { 0, 0, 16, 0, 16, 16 })]
    [InlineData(TileTransform.Rotate180, new float[] { 0, 0, 16, 0, 0, 16 })]
    public void ATransformedSlope_CollidesAsThePolygonAuthoredThatWay(TileTransform transform, float[] expected)
    {
        Vector2[] points = new Vector2[expected.Length / 2];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = new Vector2(expected[index * 2], expected[(index * 2) + 1]);
        }

        TileMap map = new(new TileGrid(
            Size,
            2,
            1,
            [
                TileGrid.EmptyTile,
                new TileDefinition("slope", null, "solid", CollisionFixtures.SlopeUp),
                new TileDefinition("authored", null, "solid", Shape2D.Polygon(points)),
            ],
            [0, 2]));
        Scene scene = new();
        scene.Add(map);
        using SceneSimulation simulation = new(scene);

        map.SetTile(0, 0, "slope", transform);

        GridCollider2D grid = map.Collision!;
        Assert.Equal(Sorted(grid.EdgesAt(1, 0)), Sorted(grid.EdgesAt(0, 0)));
        Assert.Equal(transform, map.TransformAt(0, 0));
        Assert.Equal("slope", map.TileAt(0, 0));
    }

    // Where the renderer puts each texel of a drawn tile is where the transform puts that point. The
    // corners are taken through the renderer's own vertex placement, so a mirror composed on the
    // wrong side of the quarter turn fails here.
    [Theory]
    [InlineData(TileTransform.None)]
    [InlineData(TileTransform.FlipX)]
    [InlineData(TileTransform.FlipY)]
    [InlineData(TileTransform.Rotate180)]
    [InlineData(TileTransform.Transpose)]
    [InlineData(TileTransform.Rotate90)]
    [InlineData(TileTransform.Rotate270)]
    [InlineData(TileTransform.Transpose | TileTransform.FlipX | TileTransform.FlipY)]
    public void ADrawnTile_LandsEachCornerWhereItsCollisionShapeDoes(TileTransform transform)
    {
        const float texel = 1f / 64f;
        Scene scene = new();
        scene.Add(new TileMap(new TileGrid(
            Size,
            3,
            1,
            [TileGrid.EmptyTile, new TileDefinition("tile", 1)],
            [0, 1, 0],
            SceneFixtures.Atlas,
            2,
            [TileTransform.None, transform, TileTransform.None])));
        SceneFixtures.Open(scene, new Vector2(1.5f * Size, Size / 2f), new Vector2(3 * Size, Size));
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        SpriteIntent intent = Assert.Single(simulation.View.Sprites.ToArray());
        TextureRegion region = intent.Sprite.Region;
        SpriteQuad quad = SpriteQuad.Place(
            intent.Position,
            intent.DrawOrigin,
            Vector2.One,
            region.X,
            region.Y,
            region.Width,
            region.Height,
            texel,
            texel,
            intent.Rotation,
            intent.FlipX,
            intent.FlipY);

        Vector2 cell = new(Size, 0f);
        Vector2 regionCorner = new(region.X, region.Y);
        (Vector2 Drawn, Vector2 Texture)[] corners =
        [
            (quad.TopLeft, quad.TexTopLeft),
            (quad.TopRight, new Vector2(quad.TexBottomRight.X, quad.TexTopLeft.Y)),
            (quad.BottomLeft, new Vector2(quad.TexTopLeft.X, quad.TexBottomRight.Y)),
            (quad.BottomRight, quad.TexBottomRight),
        ];
        foreach ((Vector2 drawn, Vector2 texture) in corners)
        {
            Vector2 source = (texture / texel) - regionCorner;
            Vector2 expected = TileTransforms.Apply(source, Size, transform);

            Assert.Equal(expected.X, drawn.X - cell.X, 0.001f);
            Assert.Equal(expected.Y, drawn.Y - cell.Y, 0.001f);
        }
    }

    private static CellEdge2D[] Sorted(ReadOnlySpan<CellEdge2D> edges) =>
        [.. edges.ToArray().OrderBy(edge => edge.Start.X).ThenBy(edge => edge.Start.Y)];
}
