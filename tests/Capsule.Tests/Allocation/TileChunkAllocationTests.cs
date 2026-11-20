using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tests.Tiles;
using Capsule.Tiles;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class TileChunkAllocationTests
{
    // A view that retains its tiles, moved into each of 80 chunks in turn past the 64 kept, with each frame's chunks
    // resolved as a host draws them. Every chunk a frame enters takes the slot of one drawn longest ago.
    [Fact]
    public void RetainedChunks_ViewedPastTheCap_AllocateNothingOnceWarm()
    {
        Scene scene = new();
        SceneFixtures.Open(scene, Vector2.Zero, new Vector2(200f, 120f));
        scene.Add(new TileMap(TileChunkTests.Grid(320, 256)));
        using SceneSimulation simulation = new(scene, null, null, new ViewLender { RetainsDrawState = true });
        using TileChunks chunks = new(checks: false);
        TextureSlice page = TileChunkTests.Slice(0, 0);
        InputState input = new(new ActionBindings());

        long bytes = StepMeasurement.Bytes(
            step =>
            {
                scene.Camera.Teleport((new Vector2(step % 10, step / 10 % 8) + new Vector2(0.5f)) * TileRun.ChunkSize * 8f);
                simulation.Step(new StepContext(StepMeasurement.StepSeconds, input, step));
                FrameView view = simulation.View;
                chunks.BeginFrame();
                foreach (StoredPolygon polygon in view.Polygons)
                {
                    chunks.Resolve(in view.TileRuns[polygon.TileRunIndex], polygon.Color, polygon.Flash, in page);
                }
            },
            warmup: 160,
            measured: 400);

        Assert.Equal(0, bytes);
        Assert.Equal(64, chunks.Count);
    }
}
