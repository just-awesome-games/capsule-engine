using System.Numerics;
using Capsule.Scenes;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

public sealed class EntityTreeOrderTests
{
    [Fact]
    public void ARootThatLeavesAndReturnsRepeatedly_HoldsOneEntryAtTheEndOfTheRoots()
    {
        Scene scene = new();
        Node first = new(Vector2.Zero);
        Node traveller = new(Vector2.Zero);
        Node last = new(Vector2.Zero);
        scene.Add(first);
        scene.Add(traveller);
        scene.Add(last);

        for (int cycle = 0; cycle < 20; cycle++)
        {
            traveller.Parent = first;
            traveller.Parent = null;
        }

        Assert.Equal<Entity>([first, last, traveller], scene.Entities.ToArray());
    }

    [Fact]
    public void SlotsEqualIndices_AfterInterleavedInsertsVacatesAndRebuilds()
    {
        Scene scene = new();
        Node[] early = [.. Enumerable.Range(0, 12).Select(_ => new Node(Vector2.Zero))];
        foreach (Node node in early)
        {
            scene.Add(node);
        }

        for (int index = 1; index < early.Length; index += 2)
        {
            scene.Remove(early[index]);
        }

        Node[] late = [.. Enumerable.Range(0, 6).Select(_ => new Node(Vector2.Zero))];
        foreach (Node node in late)
        {
            scene.Add(node);
        }

        Node child = new(Vector2.Zero) { Parent = early[0] };
        late[1].Parent = early[0];
        early[2].Parent = early[4];
        early[2].Parent = null;
        late[3].Parent = early[4];

        Entity[] expected =
        [
            early[0], child, late[1], early[4], late[3], early[6], early[8], early[10],
            late[0], late[2], late[4], late[5], early[2],
        ];
        ReadOnlySpan<Entity> held = scene.Entities;
        Assert.Equal(expected, held.ToArray());
        for (int index = 0; index < held.Length; index++)
        {
            Assert.Equal(index, held[index].SceneSlot);
        }
    }
}
