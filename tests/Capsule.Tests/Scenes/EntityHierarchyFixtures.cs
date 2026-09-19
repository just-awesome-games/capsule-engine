using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

internal static class EntityHierarchyFixtures
{
    internal const float Tolerance = 1e-5f;

    internal static int[] Order(SceneSimulation simulation)
    {
        ReadOnlySpan<SpriteIntent> sprites = simulation.View.Sprites;
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
