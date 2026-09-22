using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Runtime;
using Capsule.UI;

namespace Capsule.Tests.Scenes;

[Collection(LogSinkCollection.Name)]
public sealed class SceneSimulationDiagnosticsTests : IDisposable
{
    public void Dispose() => Log.UseSink(null);

    // A scene whose camera never gets a positive viewport renders black with no explanation today.
    // The world layer having content is the true predicate: it is what nothing can draw.
    [Fact]
    public void AWorldRendererUnderAZeroViewport_WarnsOnceAcrossSeveralFrames()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));

        SceneFixtures.HookScene scene = new();
        scene.Add(drifter);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step());

        LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("ViewportSize", entry.Message, StringComparison.Ordinal);
    }

    // MainMenu and Options in the sample are class-only scenes that never touch the camera and run
    // at a zero viewport by design: their content is on the screen layer, culled against Run.Canvas
    // instead. A check keyed on "no camera configured" would warn on every boot.
    [Fact]
    public void AScreenOnlySceneUnderAZeroViewport_DoesNotWarn()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        ScreenLabel label = new();
        label.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));

        SceneFixtures.HookScene scene = new();
        scene.Add(label);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Empty(sink.Entries);
    }

    private sealed class ScreenLabel() : ScreenEntity(Anchor.TopLeft, Vector2.Zero)
    {
    }
}
