using System.Numerics;
using Capsule.Animation;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Particles;

public sealed class ParticleFrameTests
{
    // A particle's whole run of frames, one cell index a step until it dies. Clip mode holds each frame its
    // authored steps whatever the lifetime. A long life never stretches it, a short one cuts it, and a loop wraps.
    [Theory]
    [InlineData(false, 1f, new[] { 0, 0, 1, 1, 1, 2 })]
    [InlineData(false, 5f, new[] { 0, 0, 1, 1, 1, 2 })]
    [InlineData(false, 0.05f, new[] { 0, 0, 1 })]
    [InlineData(true, 0.13f, new[] { 0, 0, 1, 1, 1, 2, 0, 0 })]
    public void AClipAtItsOwnTicks_HoldsEachFrameItsAuthoredSteps(bool loop, float lifetime, int[] expected)
    {
        SpriteClip clip = new([SceneFixtures.Frame(0), SceneFixtures.Frame(1), SceneFixtures.Frame(2)], [2, 3, 1], loop);

        Assert.Equal(expected, Cells(new ParticleEmitter(clip, capacity: 1) { Lifetime = (lifetime, lifetime) }));
    }

    // OverLife scales the clip's own timing to the particle's life: 1 and 3 steps over 8 hold 2 and 6.
    [Fact]
    public void OverLife_StretchesTheClipsTimingToTheParticlesLife()
    {
        SpriteClip clip = new([SceneFixtures.Frame(0), SceneFixtures.Frame(1)], [1, 3]);

        ParticleEmitter emitter = new(clip, capacity: 1) { FrameMode = FrameMode.OverLife, Lifetime = (0.13f, 0.13f) };

        Assert.Equal([0, 0, 1, 1, 1, 1, 1, 1], Cells(emitter));
    }

    [Fact]
    public void RandomAtSpawn_KeepsEachParticlesFrameForItsLife()
    {
        SpriteClip clip = new([SceneFixtures.Frame(0), SceneFixtures.Frame(1), SceneFixtures.Frame(2)], [1, 1, 1]);
        ParticleEmitter emitter = new(clip, capacity: 16) { FrameMode = FrameMode.RandomAtSpawn };
        SimulationHost host = Host(emitter, new EntityHierarchyFixtures.Node(Vector2.Zero));

        emitter.Emit(16);
        host.Step();
        int[] first = [.. host.Simulation.View.Sprites.ToArray().Select(intent => Cell(intent))];
        host.Step(5);

        Assert.Equal(first, host.Simulation.View.Sprites.ToArray().Select(intent => Cell(intent)));
        Assert.True(first.Distinct().Count() > 1, "every particle drew one frame");
    }

    // InOrderAtSpawn deals the frames in turn and wraps after the last, and a new scene life deals from the first again.
    [Fact]
    public void InOrderAtSpawn_DealsTheFramesInTurn_FromTheFirstEachLife()
    {
        SpriteClip clip = new([SceneFixtures.Frame(0), SceneFixtures.Frame(1), SceneFixtures.Frame(2)], [1, 1, 1]);
        ParticleEmitter emitter = new(clip, capacity: 8) { FrameMode = FrameMode.InOrderAtSpawn };
        EntityHierarchyFixtures.Node entity = new(Vector2.Zero);
        SimulationHost host = Host(emitter, entity);

        emitter.Emit(5);
        host.Step();
        Assert.Equal([0, 1, 2, 0, 1], host.Simulation.View.Sprites.ToArray().Select(intent => Cell(intent)));

        Scene scene = entity.Scene;
        scene.Remove(entity);
        host.Step();
        scene.Add(entity);
        emitter.Emit(2);
        host.Step();
        Assert.Equal([0, 1], host.Simulation.View.Sprites.ToArray().Select(intent => Cell(intent)));
    }

    // The entity's mirror and FlipX each mirror a spawn's offset, launch, spin and frame, and two cancel. A live
    // particle keeps its spawn's mirror after both are cleared. A local particle takes FlipX the same way.
    [Theory]
    [InlineData(ParticleSpace.World, -1f, false, true)]
    [InlineData(ParticleSpace.World, 1f, true, true)]
    [InlineData(ParticleSpace.World, -1f, true, false)]
    [InlineData(ParticleSpace.Local, 1f, true, true)]
    public void AMirror_MirrorsASpawn_AndALiveParticleKeepsIt(ParticleSpace space, float scaleX, bool flipX, bool mirrored)
    {
        EntityHierarchyFixtures.Node entity = new(Vector2.Zero) { Scale = new Vector2(scaleX, 1f) };
        ParticleEmitter emitter = new(SceneFixtures.Frame(0), capacity: 1)
        {
            Space = space,
            Offset = new Vector2(3f, 0f),
            Speed = (60f, 60f),
            AngularVelocity = (60f, 60f),
            Lifetime = (1f, 1f),
            FlipX = flipX,
        };
        SimulationHost host = Host(emitter, entity);
        float side = mirrored ? -1f : 1f;

        emitter.Emit(1);
        host.Step();
        entity.Scale = Vector2.One;
        emitter.FlipX = false;
        host.Step(2);

        SpriteIntent intent = host.Simulation.View.Sprites.ToArray().Single();
        Assert.Equal(mirrored, intent.FlipX);
        Assert.Equal(side, MathF.Sign(intent.Position.X));
        Assert.Equal(side, MathF.Sign(intent.Position.X - intent.PreviousPosition.X));
        Assert.Equal(side, MathF.Sign(intent.Rotation - intent.PreviousRotation));
    }

    // Emitted in the first step, ahead of the emitter's own. Its first draw is then its spawn's.
    private static int[] Cells(ParticleEmitter emitter)
    {
        EntityHierarchyFixtures.Node entity = new(Vector2.Zero);
        entity.Add(emitter);
        SimulationHost host = new(new SceneFixtures.HookScene(
            start: s => s.Add(entity),
            step: (Scene _, in StepContext context) =>
            {
                if (context.Tick == 0)
                {
                    emitter.Emit(1);
                }
            }));

        List<int> cells = [];
        for (host.Step(); host.Simulation.View.Sprites.ToArray().Length > 0; host.Step())
        {
            cells.Add(Cell(host.Simulation.View.Sprites.ToArray().Single()));
        }

        return [.. cells];
    }

    private static int Cell(in SpriteIntent intent) => intent.Sprite.Region.X / 8;

    private static SimulationHost Host(ParticleEmitter emitter, Entity entity)
    {
        entity.Add(emitter);

        return new SimulationHost(new SceneFixtures.HookScene(start: s => s.Add(entity)));
    }
}
