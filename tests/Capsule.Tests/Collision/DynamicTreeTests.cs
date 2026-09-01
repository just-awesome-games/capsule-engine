using System.Numerics;
using Capsule.Collision;
using Capsule.Collision.Internal;

namespace Capsule.Tests.Collision;

public sealed class DynamicTreeTests
{
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

    private static Aabb2D Cell(int x, int y) =>
        Aabb2D.FromCorner(new Vector2(x * 10f, y * 10f), new Vector2(8f, 8f));

    private static int[] Found(DynamicTree tree, in Aabb2D box)
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
