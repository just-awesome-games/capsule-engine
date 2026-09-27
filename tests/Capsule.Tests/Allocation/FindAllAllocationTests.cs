using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class FindAllAllocationTests
{
    private interface IMarked;

    // A game groups entities by an interface it declares. The walk yields exactly those, in step order,
    // and a foreach over it allocates nothing.
    [Fact]
    public void FindAllOverAnInterface_YieldsEveryEntityImplementingIt_AndAllocatesNothing()
    {
        Scene scene = new();
        Marked first = new();
        Marked second = new();
        scene.Add(first);
        scene.Add(new Plain());
        scene.Add(second);
        using SimulationHost run = new(scene);

        List<IMarked> found = [];
        foreach (IMarked marked in scene.FindAll<IMarked>())
        {
            found.Add(marked);
        }

        Assert.Equal<IMarked>([first, second], found);
        Assert.Same(second, scene.FindFirst<IMarked>(marked => marked != first));

        int count = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (IMarked marked in scene.FindAll<IMarked>())
        {
            count++;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(2, count);
    }

    private sealed class Marked() : Entity(Vector2.Zero), IMarked;

    private sealed class Plain() : Entity(Vector2.Zero);
}
