using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

internal static class EntityHierarchyFixtures
{
    internal const float Tolerance = 1e-5f;

    // A tagged renderer draws its entity's origin offset by its tag. An intent's X then names it.
    internal static SpriteRenderer Tag(int tag, int zIndex = 0) =>
        new(SceneFixtures.Frame(1, 1)) { Offset = new Vector2(tag, 0f), ZIndex = zIndex };

    internal static int[] Order(SceneSimulation simulation) => Order(simulation.View.Sprites);

    internal static int[] Order(ReadOnlySpan<SpriteIntent> sprites)
    {
        int[] tags = new int[sprites.Length];
        for (int index = 0; index < tags.Length; index++)
        {
            tags[index] = (int)sprites[index].Position.X;
        }

        return tags;
    }

    internal sealed class Node(Vector2 position) : Entity(position);

    internal sealed class Mover : Component
    {
        protected internal override void OnStep(in StepContext context) => Entity!.Position += Vector2.UnitX;
    }
}
