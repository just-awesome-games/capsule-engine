using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Diagnostics;

// A draw knows which entity's pass made it. A host uses that to tell one entity's draws from another's.
[Collection(LogSinkCollection.Name)]
public sealed class DebugDrawOwnerTests
{
    [Fact]
    public void ADrawFromAnEntitysPass_CarriesThatEntityAndAnyOtherDrawCarriesNone()
    {
        DebugDrawBuffer buffer = new();
        DebugDraw.UseBuffer(buffer);
        try
        {
            Ringed ringed = new();
            Scene scene = new Drawing();
            scene.Add(ringed);
            SceneSimulation simulation = new(scene);

            simulation.Step(SceneFixtures.Step());

            Assert.Equal(1, Count(buffer, "scene", null));
            Assert.Equal(1, Count(buffer, "step", null));

            // The origin cross is two segments, and the entity and its component draw one each.
            Assert.Equal(2, Count(buffer, DebugDraw.Origins, ringed));
            Assert.Equal(1, Count(buffer, "entity", ringed));
            Assert.Equal(1, Count(buffer, "component", ringed));
            Assert.Equal(1, buffer.Labels.Length);
            Assert.Same(ringed, buffer.Labels[0].Owner);
            Assert.All(buffer.Segments.ToArray(), static segment => Assert.True(segment.Owner is null or Ringed));

            DebugDraw.Line("later", Vector2.Zero, Vector2.One);

            Assert.Equal(1, Count(buffer, "later", null));
        }
        finally
        {
            DebugDraw.UseBuffer(null);
        }
    }

    private static int Count(DebugDrawBuffer buffer, string channel, object? owner)
    {
        int count = 0;
        foreach (DebugDrawSegment segment in buffer.Segments)
        {
            if (segment.Channel == channel && ReferenceEquals(segment.Owner, owner))
            {
                count++;
            }
        }

        return count;
    }

    private sealed class Drawing : Scene
    {
        protected override void OnStep(in StepContext context) =>
            DebugDraw.Line("step", Vector2.Zero, Vector2.One);

        protected override void OnDebugDraw() =>
            DebugDraw.Line("scene", Vector2.Zero, Vector2.One);
    }

    private sealed class Ringed : Entity
    {
        internal Ringed() => Add(new Marking());

        protected internal override void OnDebugDraw()
        {
            DebugDraw.Line("entity", Vector2.Zero, Vector2.One);
            DebugDraw.Text("entity", Vector2.Zero, "hi");
        }
    }

    private sealed class Marking : Component
    {
        protected internal override void OnDebugDraw() =>
            DebugDraw.Line("component", Vector2.Zero, Vector2.One);
    }
}
