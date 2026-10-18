using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Scenes;
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

        using SceneSimulation simulation = new(new Scene());
        Scene scene = simulation.Scene;
        scene.Add(first);
        LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("BoxCollider2D on a Drifter", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Set Detects to the layers it should report", entry.Message, StringComparison.Ordinal);

        scene.Add(second);
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

    // A contact handler on a collider whose ReportsContacts is off never fires. Registering warns once a
    // lifetime, and a collider that reports contacts or has no handler stays quiet.
    [Fact]
    public void AContactHandlerWithReportsContactsOff_WarnsOnceOnRegistering()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        BoxCollider2D unheard = new(new Vector2(8f, 8f));
        unheard.ContactExited += static _ => { };
        BoxCollider2D reporting = new(new Vector2(8f, 8f))
        {
            ReportsContacts = true,
            Detects = new(CollisionFixtures.Solid),
        };
        reporting.ContactEntered += static _ => { };
        SceneFixtures.Drifter first = new(Vector2.Zero);
        SceneFixtures.Drifter second = new(Vector2.Zero);
        SceneFixtures.Drifter third = new(Vector2.Zero);
        first.Add(unheard);
        second.Add(reporting);
        third.Add(new BoxCollider2D(new Vector2(8f, 8f)));

        using SceneSimulation simulation = new(new Scene());
        Scene scene = simulation.Scene;
        scene.Add(second);
        scene.Add(third);
        Assert.Empty(sink.Entries);

        scene.Add(first);
        LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("BoxCollider2D on a Drifter", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Set ReportsContacts = true", entry.Message, StringComparison.Ordinal);

        scene.Remove(first);
        scene.Add(first);
        Assert.Single(sink.Entries);
    }

    // A body that nothing blocks passes through every wall. Its first move warns, since masks are often
    // set after the body joins, and a body given BlockedBy or MovedBy by then stays quiet.
    [Fact]
    public void ABodyWithEmptyBlockingMasks_WarnsOnItsFirstMoveOnly()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);
        Scene scene = new Scene().Started();
        SceneFixtures.Body unblocked = new(Vector2.Zero);
        SceneFixtures.Body blocked = new(new Vector2(40f, 0f));
        SceneFixtures.Body moved = new(new Vector2(80f, 0f));
        scene.Add(unblocked);
        scene.Add(blocked);
        scene.Add(moved);

        blocked.Mover.BlockedBy = new(CollisionFixtures.Solid);
        moved.Mover.MovedBy = new(CollisionFixtures.Solid);
        blocked.Mover.Move(Vector2.UnitX);
        moved.Mover.Move(Vector2.UnitX);
        Assert.Empty(sink.Entries);

        unblocked.Mover.Move(Vector2.UnitX);
        unblocked.Mover.Move(Vector2.UnitX);
        LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("KinematicBody2D on a Body", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Set BlockedBy to the layers that should", entry.Message, StringComparison.Ordinal);
    }
}
