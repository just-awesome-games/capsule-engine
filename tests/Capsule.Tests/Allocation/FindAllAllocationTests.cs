using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class FindAllAllocationTests
{
    private interface IMarked;

    // A game groups entities by an interface it declares. The walk yields exactly those, in step order,
    // and neither a foreach over it nor a find by name allocates. An unnamed entity answers to its type name.
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

        Assert.Same(first, scene.FindFirst<IMarked>(nameof(Marked)));

        int count = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (IMarked _ in scene.FindAll<IMarked>())
        {
            count++;
        }

        IMarked? named = scene.FindFirst<IMarked>(nameof(Marked));

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(2, count);
        Assert.Same(first, named);
    }

    private sealed class Marked() : Entity(Vector2.Zero), IMarked;

    private sealed class Plain() : Entity(Vector2.Zero);
}
