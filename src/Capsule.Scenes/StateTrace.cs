using System.Globalization;
using System.Numerics;

namespace Capsule.Scenes;

/// <summary>
/// What a run's world looked like at the end of every fixed step, accumulated in memory and
/// written as long-form CSV — one row per value, so runs diff line by line and any reader pivots
/// them into whatever shape it wants. Given to a <see cref="SceneSimulation"/> at construction;
/// a run without one costs one null check per step.
/// <para>
/// Every step records, for each entity the scene holds in scene order, its position as
/// <c>x</c> and <c>y</c> and its runtime type name as <c>type</c>, then the columns the entity
/// and each of its components write when they implement <see cref="ITraceSource"/>; the camera's
/// centre follows as <c>x</c> and <c>y</c> under the subject <c>camera</c>.
/// </para>
/// <para>
/// A subject names an entity for the whole run: the id of the document placement that spawned it,
/// written <c>#12</c>, or, for an entity created in code, an ordinal assigned when the trace first
/// sees it and written <c>e3</c>. An entity removed from the scene simply stops appearing, and one
/// added back keeps the subject it had. Ordinals are unique across the run; document ids belong to
/// their own document's id space, so two scenes of one run may repeat a subject.
/// </para>
/// <para>
/// The trace grows with the run: every row it took and every subject it named are held until it is
/// released. It performs no I/O — the caller decides where <see cref="Write(TextWriter)"/> goes.
/// </para>
/// </summary>
public sealed class StateTrace
{
    /// <summary>The subject the scene's camera is recorded under.</summary>
    public const string CameraSubject = "camera";

    private const string Header = "tick,subject,column,value";

    private readonly List<Row> _rows = [];

    // By reference, never by Equals: a game may give two distinct entities an equality of its own
    // and both must still hold their own identity for the length of the run.
    private readonly Dictionary<Entity, string> _subjects = new(ReferenceEqualityComparer.Instance);

    private int _nextOrdinal;

    /// <summary>
    /// Writes the header row and every row recorded so far, each line terminated with <c>\n</c> in
    /// the invariant culture. A field holding a comma, a quote or a newline is quoted per RFC 4180.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is null.</exception>
    public void Write(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Header);
        writer.Write('\n');

        foreach (Row row in _rows)
        {
            writer.Write(row.Tick.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            WriteField(writer, row.Subject);
            writer.Write(',');
            WriteField(writer, row.Column);
            writer.Write(',');
            WriteField(writer, row.Value);
            writer.Write('\n');
        }
    }

    /// <summary>The CSV <see cref="Write(TextWriter)"/> produces, as one string.</summary>
    public override string ToString()
    {
        StringWriter writer = new(CultureInfo.InvariantCulture);
        Write(writer);

        return writer.ToString();
    }

    // The shortest text that round-trips value, in the invariant culture.
    internal static string Text(float value) => value.ToString(CultureInfo.InvariantCulture);

    internal void Add(long tick, string subject, string column, string value) =>
        _rows.Add(new Row(tick, subject, column, value));

    // Everything scene holds at the end of tick. Entities in scene order and the camera last, so
    // one tick's rows are contiguous and ordered the same way every run.
    internal void Capture(Scene scene, long tick)
    {
        foreach (Entity entity in scene.Entities)
        {
            string subject = SubjectOf(entity);
            Vector2 position = entity.Position;

            Add(tick, subject, "x", Text(position.X));
            Add(tick, subject, "y", Text(position.Y));
            Add(tick, subject, "type", entity.GetType().Name);

            TraceWriter writer = new(this, subject, tick);
            if (entity is ITraceSource source)
            {
                source.WriteTrace(writer);
            }

            foreach (Component component in entity.Components)
            {
                if (component is ITraceSource contributor)
                {
                    contributor.WriteTrace(writer);
                }
            }
        }

        Vector2 center = scene.Camera.Center;
        Add(tick, CameraSubject, "x", Text(center.X));
        Add(tick, CameraSubject, "y", Text(center.Y));
    }

    private static void WriteField(TextWriter writer, string field)
    {
        if (field.AsSpan().IndexOfAny(",\"\r\n") < 0)
        {
            writer.Write(field);
            return;
        }

        writer.Write('"');
        writer.Write(field.Replace("\"", "\"\"", StringComparison.Ordinal));
        writer.Write('"');
    }

    private string SubjectOf(Entity entity)
    {
        if (_subjects.TryGetValue(entity, out string? subject))
        {
            return subject;
        }

        subject = entity.DocumentId is { } id
            ? string.Create(CultureInfo.InvariantCulture, $"#{id}")
            : string.Create(CultureInfo.InvariantCulture, $"e{_nextOrdinal++}");

        _subjects[entity] = subject;

        return subject;
    }

    private readonly record struct Row(long Tick, string Subject, string Column, string Value);
}
