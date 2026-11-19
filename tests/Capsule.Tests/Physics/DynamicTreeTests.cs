using System.Numerics;
using Capsule.Physics;
using Capsule.Physics.Internal;

namespace Capsule.Tests.Physics;

public sealed class DynamicTreeTests
{
    private const ulong Red = 1UL << 0;
    private const ulong Blue = 1UL << 1;
    private const ulong Green = 1UL << 2;
    private const ulong Everything = ulong.MaxValue;

    [Fact]
    public void MoveProxy_ReinsertsOnlyWhenTheTightBoundsEscapeTheFatOnes()
    {
        DynamicTree2D tree = new();
        int proxy = tree.CreateProxy(Cell(0, 0), 0, Red);

        Assert.False(tree.MoveProxy(proxy, Cell(0, 0).Translated(new Vector2(0.5f, 0f)), new Vector2(0.5f, 0f)));
        Assert.True(tree.MoveProxy(proxy, Cell(0, 0).Translated(new Vector2(40f, 0f)), new Vector2(40f, 0f)));
        Assert.Equal([0], Found(tree, Cell(4, 0), Everything));
        Assert.Empty(Found(tree, Cell(0, 0), Everything));
    }

    // A far jump is a teleport, not travel. The slack it leaves ahead of the landing stops at the cap
    // and does not grow with the jump.
    [Fact]
    public void MoveProxy_CapsTheLookaheadAFarJumpLeavesAheadOfTheLanding()
    {
        DynamicTree2D tree = new();
        int proxy = tree.CreateProxy(Cell(0, 0), 0, Red);

        Assert.True(tree.MoveProxy(proxy, Cell(100, 0), new Vector2(1000f, 0f)));
        Assert.Equal([0], Found(tree, Cell(100, 0), Everything));
        Assert.Empty(Found(tree, Cell(108, 0), Everything));
    }

    // The rotations a thousand inserts force rewrite parentage, each layer's root among it. A root handed
    // to the wrong layer's tree loses proxies rather than merely visiting extra ones. The first two
    // proxies index both layers, and the rest are inserted.
    [Fact]
    public void Query_KeepsEachLayerExactThroughTheRotationsAThousandInsertsForce()
    {
        DynamicTree2D tree = new();
        tree.CreateProxy(Cell(0, 0), 0, Red);
        tree.CreateProxy(Cell(1, 0), 1, Blue);
        Assert.Equal([0, 1], Found(tree, All, Red | Blue));
        for (int index = 2; index < 1024; index++)
        {
            tree.CreateProxy(Cell(index % 32, index / 32), index, (index % 2) == 0 ? Red : Blue);
        }

        Assert.Equal(Evens(1024), Found(tree, All, Red));
        Assert.Equal(Odds(1024), Found(tree, All, Blue));
    }

    // Every path that moves a proxy between or within the layers' trees: a leaf left in its old layer's
    // tree, or a root left pointing at a removed node, hides a proxy the query should have reached.
    [Fact]
    public void LayerWritingPaths_LeaveEveryProxyFindableThroughMovesRewritesAndRemovals()
    {
        DynamicTree2D tree = new();
        int[] proxies = new int[300];
        for (int index = 0; index < proxies.Length; index++)
        {
            proxies[index] = tree.CreateProxy(Cell(index % 20, index / 20), index, Layer(index));
        }

        Assert.Equal(Range(300), Found(tree, All, Red | Blue | Green));

        for (int index = 0; index < proxies.Length; index += 3)
        {
            Assert.True(tree.MoveProxy(proxies[index], Cell(40 + (index % 20), index / 20), new Vector2(400f, 0f)));
        }

        Assert.Equal(Range(300), Found(tree, All, Red | Blue | Green));

        for (int index = 0; index < proxies.Length; index += 5)
        {
            tree.SetProxyMask(proxies[index], Green);
        }

        Assert.Equal(Range(300), Found(tree, All, Red | Blue | Green));

        // Every Blue proxy rewritten to Red. Blue's tree empties and has to stop answering.
        for (int index = 0; index < proxies.Length; index++)
        {
            if (Layer(index) == Blue)
            {
                tree.SetProxyMask(proxies[index], Red);
            }
        }

        Assert.Equal(Range(300), Found(tree, All, Red | Green));
        Assert.Empty(Found(tree, All, Blue));

        // Asserted per removal. The promotions that reach a grandparent or the root are each
        // covered, down to a tree holding one proxy.
        for (int index = 0; index < proxies.Length - 1; index++)
        {
            tree.DestroyProxy(proxies[index]);
            Assert.Equal(
                [.. Enumerable.Range(index + 1, proxies.Length - index - 1)],
                Found(tree, All, Red | Green));
        }
    }

    // A walk builds the loose proxies of the layers it names, and only those. Moves, rewrites, removals
    // and a walk naming an empty layer build nothing.
    [Fact]
    public void AWalk_BuildsTheLooseLayersItNamesAndNoOthers()
    {
        DynamicTree2D tree = new();
        int red = tree.CreateProxy(Cell(0, 0), 0, Red);
        tree.CreateProxy(Cell(1, 0), 1, Red);
        tree.CreateProxy(Cell(2, 0), 2, Blue);
        Assert.True(tree.MoveProxy(red, Cell(60, 0), new Vector2(600f, 0f)));
        Assert.True(tree.Holds(Red | Blue));
        Assert.Empty(Found(tree, All, Green));

        Assert.Equal(0UL, tree.IndexedLayers);
        Assert.Equal(Red | Blue, tree.LooseLayers);

        Assert.Equal([0, 1], Found(tree, All, Red | Green));
        Assert.Equal(Red, tree.IndexedLayers);
        Assert.Equal(Blue, tree.LooseLayers);
    }

    // An indexed layer takes a new proxy straight into its tree, and stays indexed once it empties.
    [Fact]
    public void AProxyCreatedOnAnIndexedLayer_IsInsertedAtOnceEvenAfterTheLayerEmpties()
    {
        DynamicTree2D tree = new();
        int first = tree.CreateProxy(Cell(0, 0), 0, Red);
        Assert.Equal([0], Found(tree, All, Red));

        int second = tree.CreateProxy(Cell(1, 0), 1, Red);
        Assert.Equal(0UL, tree.LooseLayers);
        Assert.Equal([0, 1], Found(tree, All, Red));

        tree.DestroyProxy(first);
        tree.DestroyProxy(second);
        Assert.False(tree.Holds(Red));
        tree.CreateProxy(Cell(2, 0), 2, Red);

        Assert.Equal(Red, tree.IndexedLayers);
        Assert.Equal(0UL, tree.LooseLayers);
        Assert.Equal([2], Found(tree, All, Red));
    }

    // Unlinking a loose proxy at the head, middle or tail of its layer's list keeps the rest for the
    // build. A layer whose loose proxies all leave, removed or rewritten, has nothing left to build.
    [Fact]
    public void LooseProxiesLeavingTheirLayer_LeaveTheRestToBuildAndAnEmptiedLayerUnbuilt()
    {
        DynamicTree2D tree = new();
        tree.CreateProxy(Cell(0, 1), 100, Red);
        Assert.Equal([100], Found(tree, All, Red));

        int[] blue = new int[6];
        for (int index = 0; index < blue.Length; index++)
        {
            blue[index] = tree.CreateProxy(Cell(index, 0), index, Blue);
        }

        tree.DestroyProxy(blue[5]);
        tree.DestroyProxy(blue[2]);
        tree.DestroyProxy(blue[0]);

        int removed = tree.CreateProxy(Cell(0, 2), 200, Green);
        int rewritten = tree.CreateProxy(Cell(1, 2), 201, Green);
        tree.DestroyProxy(removed);
        tree.SetProxyMask(rewritten, Red);
        Assert.Equal(Blue, tree.LooseLayers);

        Assert.Empty(Found(tree, All, Green));
        Assert.Equal(Red, tree.IndexedLayers);
        Assert.Equal([1, 3, 4], Found(tree, All, Blue));
        Assert.Equal([100, 201], Found(tree, All, Red));
    }

    // A proxy rewritten from an indexed layer to an unindexed one leaves the tree and waits loose, and
    // one rewritten the other way is inserted at once.
    [Fact]
    public void Relayering_BetweenIndexedAndUnindexedLayers_KeepsEveryQueryExact()
    {
        DynamicTree2D tree = new();
        int[] red = new int[4];
        int[] blue = new int[4];
        for (int index = 0; index < 4; index++)
        {
            red[index] = tree.CreateProxy(Cell(index, 0), index, Red);
            blue[index] = tree.CreateProxy(Cell(index, 1), 10 + index, Blue);
        }

        Assert.Equal([0, 1, 2, 3], Found(tree, All, Red));

        tree.SetProxyMask(red[0], Blue);
        tree.SetProxyMask(blue[0], Red);
        Assert.Equal(Blue, tree.LooseLayers);
        Assert.Equal([1, 2, 3, 10], Found(tree, All, Red));

        Assert.Equal([0, 11, 12, 13], Found(tree, All, Blue));
        tree.SetProxyMask(red[0], Red);
        tree.SetProxyMask(blue[1], Red);
        Assert.Equal([0, 1, 2, 3, 10, 11], Found(tree, All, Red));
        Assert.Equal([12, 13], Found(tree, All, Blue));
    }

    // A loose proxy can be moved, rewritten and removed before the first walk, and a later crowd joins a
    // layer whose tree is built one proxy at a time while another layer's is built whole.
    [Fact]
    public void LooseProxies_StayFindableThroughMovesRewritesAndRemovalsBeforeAndAfterTheFirstWalk()
    {
        DynamicTree2D tree = new();
        int[] proxies = new int[40];
        for (int index = 0; index < proxies.Length; index++)
        {
            proxies[index] = tree.CreateProxy(Cell(index % 8, index / 8), index, Red);
        }

        Assert.True(tree.MoveProxy(proxies[3], Cell(60, 0), new Vector2(570f, 0f)));
        tree.SetProxyMask(proxies[4], Blue);
        tree.DestroyProxy(proxies[5]);
        Assert.True(tree.Holds(Blue));

        Assert.Equal([3], Found(tree, Cell(60, 0), Red));
        Assert.Equal([4], Found(tree, All, Blue));
        Assert.Equal([.. Range(40).Except([4, 5])], Found(tree, All, Red));

        for (int index = 40; index < 60; index++)
        {
            tree.CreateProxy(Cell(index % 8, index / 8), index, (index % 2) == 0 ? Red : Green);
        }

        Assert.Equal([.. Range(40).Except([4, 5]), .. Evens(60).Where(static index => index >= 40)], Found(tree, All, Red));
        Assert.Equal(Odds(60).Where(static index => index >= 40), Found(tree, All, Green));
    }

    [Fact]
    public void AProxysLayer_IsASingleBit()
    {
        DynamicTree2D tree = new();
        int proxy = tree.CreateProxy(Cell(0, 0), 0, Red);

        Assert.Throws<ArgumentException>(() => tree.CreateProxy(Cell(1, 0), 1, Red | Blue));
        Assert.Throws<ArgumentException>(() => tree.SetProxyMask(proxy, 0));
    }

    private static Aabb2D All => Aabb2D.FromCorner(new Vector2(-1000f, -1000f), new Vector2(2000f, 2000f));

    private static ulong Layer(int index) => (index % 3) switch
    {
        0 => Red,
        1 => Blue,
        _ => Green,
    };

    private static int[] Range(int count) => [.. Enumerable.Range(0, count)];

    private static int[] Evens(int count) => [.. Enumerable.Range(0, count).Where(static index => (index % 2) == 0)];

    private static int[] Odds(int count) => [.. Enumerable.Range(0, count).Where(static index => (index % 2) != 0)];

    private static Aabb2D Cell(int x, int y) =>
        Aabb2D.FromCorner(new Vector2(x * 10f, y * 10f), new Vector2(8f, 8f));

    private static int[] Found(DynamicTree2D tree, in Aabb2D box, ulong mask)
    {
        Collector collector = new(tree);
        tree.Query(box, mask, ref collector);

        return collector.Sorted();
    }

    private struct Collector(DynamicTree2D tree) : ITreeVisitor2D
    {
        private readonly List<int> _found = [];

        public bool Visit(int proxyId)
        {
            _found.Add(tree.UserDataOf(proxyId));
            return true;
        }

        public readonly int[] Sorted()
        {
            int[] found = [.. _found];
            Array.Sort(found);

            return found;
        }
    }
}
