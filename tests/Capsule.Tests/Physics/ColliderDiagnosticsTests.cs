using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Runtime;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

[Collection(LogSinkCollection.Name)]
public sealed class ColliderDiagnosticsTests : IDisposable
{
    public void Dispose() => Log.UseSink(null);

    // A collider that reports contacts but detects nothing is a silent no-op. It warns once each
    // time it enters that state while registered: on registering, on a Detects or ReportsContacts
    // write, and on rejoining a scene. Steps and writes that keep it in the state stay quiet. A
    // collider whose initializer sets ReportsContacts before Detects never warns.
    [Fact]
    public void AColliderReportingContactsWithAnEmptyDetects_WarnsOnEachEntryIntoThatState()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        BoxCollider2D silent = new(new Vector2(8f, 8f)) { ReportsContacts = true };
        BoxCollider2D configured = new(new Vector2(8f, 8f))
        {
            ReportsContacts = true,
            Detects = new(CollisionFixtures.Solid),
        };
        SceneFixtures.Drifter first = new(Vector2.Zero);
        SceneFixtures.Drifter second = new(Vector2.Zero);
        first.Add(silent);
        second.Add(configured);
        Assert.Empty(sink.Entries);

        Scene scene = new();
        scene.Add(first);
        LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("BoxCollider2D on a Drifter", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Set Detects to the layers it should report", entry.Message, StringComparison.Ordinal);

        scene.Add(second);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));
        simulation.Step(SceneFixtures.Step(1));
        Assert.Single(sink.Entries);

        silent.Detects = new(CollisionFixtures.Solid);
        Assert.Single(sink.Entries);
        silent.Detects = new();
        Assert.Equal(2, sink.Entries.Count);

        silent.ReportsContacts = false;
        Assert.Equal(2, sink.Entries.Count);
        silent.ReportsContacts = true;
        Assert.Equal(3, sink.Entries.Count);

        scene.Remove(first);
        Assert.Equal(3, sink.Entries.Count);
        scene.Add(first);
        Assert.Equal(4, sink.Entries.Count);

        silent.Detects = new();
        Assert.Equal(4, sink.Entries.Count);
    }
}
