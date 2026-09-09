using System.Numerics;
using Capsule.Collision;
using Capsule.Collision.Internal;

namespace Capsule.Tests.Collision;

public sealed class DynamicTreeTests
{
    private const ulong Red = 1UL << 0;
    private const ulong Blue = 1UL << 1;
    private const ulong Green = 1UL << 2;
    private const ulong Everything = ulong.MaxValue;

    // A tree that never rebuilds still has to stay shallow, or a query walks a list.
    [Fact]
    public void Insert_KeepsTheTreeBalancedWithoutRebuildingIt()
    {
        DynamicTree tree = new();
        for (int index = 0; index < 1024; index++)
        {
            tree.CreateProxy(Cell(index % 32, index / 32), index, Red);
        }

        Assert.InRange(tree.Height, 10, 24);
    }

    [Fact]
    public void MoveProxy_ReinsertsOnlyWhenTheTightBoundsEscapeTheFatOnes()
    {
        DynamicTree tree = new();
        int proxy = tree.CreateProxy(Cell(0, 0), 0, Red);

        Assert.False(tree.MoveProxy(proxy, Cell(0, 0).Translated(new Vector2(0.5f, 0f)), new Vector2(0.5f, 0f)));
        Assert.True(tree.MoveProxy(proxy, Cell(0, 0).Translated(new Vector2(40f, 0f)), new Vector2(40f, 0f)));
        Assert.Equal([0], Found(tree, Cell(4, 0), Everything));
        Assert.Empty(Found(tree, Cell(0, 0), Everything));
    }

    [Fact]
    public void Query_ReachesNothingOnALayerNoProxyCarries()
    {
        DynamicTree tree = new();
        for (int index = 0; index < 32; index++)
        {
            tree.CreateProxy(Cell(index % 8, index / 8), index, Red);
        }

        Assert.Empty(Found(tree, All, Blue));
    }

    // Interleaved, so no box test can do the culling the mask is there for.
    [Fact]
    public void Query_VisitsOnlyTheProxiesOnTheMaskedLayersWhenLayersAreInterleaved()
    {
        DynamicTree tree = new();
        for (int index = 0; index < 64; index++)
        {
            tree.CreateProxy(Cell(index % 8, index / 8), index, (index % 2) == 0 ? Red : Blue);
        }

        Assert.Equal(Evens(64), Found(tree, All, Red));
        Assert.Equal(Odds(64), Found(tree, All, Blue));
        Assert.Equal(Range(64), Found(tree, All, Red | Blue));
    }

    // The rotations a thousand inserts force rewrite parentage; a mask that is not carried through
    // them loses proxies rather than merely visiting extra ones.
    [Fact]
    public void Query_KeepsTheMaskExactThroughTheRotationsAThousandInsertsForce()
    {
        DynamicTree tree = new();
        for (int index = 0; index < 1024; index++)
        {
            tree.CreateProxy(Cell(index % 32, index / 32), index, (index % 2) == 0 ? Red : Blue);
        }

        Assert.Equal(Evens(1024), Found(tree, All, Red));
        Assert.Equal(Odds(1024), Found(tree, All, Blue));
    }

    [Fact]
    public void MoveProxy_KeepsTheMaskExactAcrossAReinsertion()
    {
        DynamicTree tree = new();
        for (int index = 0; index < 16; index++)
        {
            tree.CreateProxy(Cell(index, 0), index, Red);
        }

        int traveller = tree.CreateProxy(Cell(0, 0), 99, Blue);
        Assert.True(tree.MoveProxy(traveller, Cell(0, 20), new Vector2(0f, 200f)));

        Assert.Equal([99], Found(tree, All, Blue));
        Assert.Equal(Range(16), Found(tree, All, Red));
    }

    // A rewritten layer has to leave the ancestors it reached, not merely add the new one to them.
    [Fact]
    public void SetProxyMask_TakesTheOldLayerOutOfEveryAncestorItReached()
    {
        DynamicTree tree = new();
        for (int index = 0; index < 64; index++)
        {
            tree.CreateProxy(Cell(index % 8, index / 8), index, Red);
        }

        int only = tree.CreateProxy(Cell(3, 3), 99, Blue);
        Assert.Equal([99], Found(tree, All, Blue));

        tree.SetProxyMask(only, Green);

        Assert.Empty(Found(tree, All, Blue));
        Assert.Equal([99], Found(tree, All, Green));
        Assert.Equal(Range(64), Found(tree, All, Red));
    }

    // Leaf-level filtering hides a stale ancestor bit from every query, so the union each internal
    // node's mask must equal is asserted structurally rather than through what a walk finds.
    [Fact]
    public void MaskWritingPaths_LeaveEveryInternalNodeTheUnionOfItsChildren()
    {
        DynamicTree tree = new();
        int[] proxies = new int[300];
        for (int index = 0; index < proxies.Length; index++)
        {
            proxies[index] = tree.CreateProxy(Cell(index % 20, index / 20), index, Layer(index));
        }

        AssertMasksExact(tree);

        for (int index = 0; index < proxies.Length; index += 3)
        {
            Assert.True(tree.MoveProxy(proxies[index], Cell(40 + (index % 20), index / 20), new Vector2(400f, 0f)));
        }

        AssertMasksExact(tree);

        for (int index = 0; index < proxies.Length; index += 5)
        {
            tree.SetProxyMask(proxies[index], Green);
        }

        AssertMasksExact(tree);

        // Every Blue proxy rewritten to Red, so the bit has to leave its ancestors rather than
        // merely being joined by another.
        for (int index = 0; index < proxies.Length; index++)
        {
            if (Layer(index) == Blue)
            {
                tree.SetProxyMask(proxies[index], Red);
            }
        }

        AssertMasksExact(tree);
        Assert.Empty(Found(tree, All, Blue));

        // Asserted per removal, so the promotions that reach a grandparent or the root are each
        // covered, down to a tree holding one proxy.
        for (int index = 0; index < proxies.Length - 1; index++)
        {
            tree.DestroyProxy(proxies[index]);
            AssertMasksExact(tree);
        }

        Assert.Equal(1, tree.ProxyCount);
    }

    private static Aabb2D All => Aabb2D.FromCorner(new Vector2(-1000f, -1000f), new Vector2(2000f, 2000f));

    private static ulong Layer(int index) => (index % 3) switch
    {
        0 => Red,
        1 => Blue,
        _ => Green,
    };

    private static void AssertMasksExact(DynamicTree tree) =>
        Assert.Equal(DynamicTree.NullNode, tree.FirstNodeWithInexactMask());

    private static int[] Range(int count) => [.. Enumerable.Range(0, count)];

    private static int[] Evens(int count) => [.. Enumerable.Range(0, count).Where(static index => (index % 2) == 0)];

    private static int[] Odds(int count) => [.. Enumerable.Range(0, count).Where(static index => (index % 2) != 0)];

    private static Aabb2D Cell(int x, int y) =>
        Aabb2D.FromCorner(new Vector2(x * 10f, y * 10f), new Vector2(8f, 8f));

    private static int[] Found(DynamicTree tree, in Aabb2D box, ulong mask)
    {
        Collector collector = new(tree);
        tree.Query(box, mask, ref collector);

        return collector.Sorted();
    }

    private struct Collector(DynamicTree tree) : ITreeVisitor
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
