using System.Numerics;
using Capsule.Collision;
using Capsule.Collision.Internal;

namespace Capsule.Tests.Collision;

public sealed class DynamicTreeTests
{
    [Fact]
    public void Query_FindsExactlyTheProxiesOverlappingTheBox()
    {
        DynamicTree tree = new();
        for (int index = 0; index < 400; index++)
        {
            tree.CreateProxy(Cell(index % 20, index / 20), index);
        }

        Assert.Equal(400, tree.ProxyCount);
        Assert.Equal([0, 1, 20, 21], Found(tree, new Aabb(new Vector2(9f, 9f), new Vector2(11f, 11f))));
    }

    // A tree that never rebuilds still has to stay shallow, or a query walks a list.
    [Fact]
    public void Insert_KeepsTheTreeBalancedWithoutRebuildingIt()
    {
        DynamicTree tree = new();
        for (int index = 0; index < 1024; index++)
        {
            tree.CreateProxy(Cell(index % 32, index / 32), index);
        }

        Assert.InRange(tree.Height, 10, 24);
    }

    [Fact]
    public void MoveProxy_ReinsertsOnlyWhenTheTightBoundsEscapeTheFatOnes()
    {
        DynamicTree tree = new();
        int proxy = tree.CreateProxy(Cell(0, 0), 0);

        Assert.False(tree.MoveProxy(proxy, Cell(0, 0).Translated(new Vector2(0.5f, 0f)), new Vector2(0.5f, 0f)));
        Assert.True(tree.MoveProxy(proxy, Cell(0, 0).Translated(new Vector2(40f, 0f)), new Vector2(40f, 0f)));
        Assert.Equal([0], Found(tree, Cell(4, 0)));
        Assert.Empty(Found(tree, Cell(0, 0)));
    }

    [Fact]
    public void DestroyProxy_LeavesTheRemainingProxiesFindable()
    {
        DynamicTree tree = new();
        int[] proxies = new int[64];
        for (int index = 0; index < proxies.Length; index++)
        {
            proxies[index] = tree.CreateProxy(Cell(index, 0), index);
        }

        for (int index = 0; index < proxies.Length; index += 2)
        {
            tree.DestroyProxy(proxies[index]);
        }

        Assert.Equal(32, tree.ProxyCount);
        Assert.Equal(
            [1, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25, 27, 29, 31, 33, 35, 37, 39, 41, 43, 45, 47, 49, 51, 53, 55, 57, 59, 61, 63],
            Found(tree, new Aabb(new Vector2(0f, 0f), new Vector2(640f, 8f))));
    }

    [Fact]
    public void RayCast_ReachesOnlyTheProxiesTheRayCouldTouch()
    {
        DynamicTree tree = new();
        for (int index = 0; index < 100; index++)
        {
            tree.CreateProxy(Cell(index % 10, index / 10), index);
        }

        RayCollector collector = new(tree);
        tree.RayCast(new Vector2(-100f, 5f), Vector2.UnitX, 1000f, ref collector);

        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], collector.Sorted());
    }

    private static Aabb Cell(int x, int y) =>
        Aabb.FromCorner(new Vector2(x * 10f, y * 10f), new Vector2(8f, 8f));

    private static int[] Found(DynamicTree tree, in Aabb box)
    {
        Collector collector = new(tree);
        tree.Query(box, ref collector);

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

    private struct RayCollector(DynamicTree tree) : IRayVisitor
    {
        private readonly List<int> _found = [];

        public float Visit(int proxyId, float maxFraction)
        {
            _found.Add(tree.UserDataOf(proxyId));
            return maxFraction;
        }

        public readonly int[] Sorted()
        {
            int[] found = [.. _found];
            Array.Sort(found);

            return found;
        }
    }
}
