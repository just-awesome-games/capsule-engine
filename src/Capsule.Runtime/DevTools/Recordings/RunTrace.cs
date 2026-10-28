using System.Globalization;
using Capsule.Diagnostics;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools.Recordings;

// What --trace writes from a headless run, replayed or driven: the fields and toggles the named entities'
// panels show at the named ticks, as CSV rows of tick, entity name, panel section, field and value. Scene
// and Camera name the scene's own panel and its camera's. A tick is the overlay's, the state after that
// many steps. A stretch of consecutive ticks in one scene opens with a row naming the scene by its key and
// writes every row on its first tick. A replayed overlay act writes a row at the tick its result shows. Each later tick writes only a new or changed value, an empty value for a
// field gone from a panel, and an empty section and field for an entity gone from the trace. A step
// outside the ticks costs a range check, and a traced tick allocates its rows, which no live run ever pays.
internal sealed class RunTrace : IDisposable
{
    private const string Header = "tick,entity,section,field,value";

    // A selector matching every entity, the scene and its camera.
    private const string Everything = "*";

    // The selectors, and the entity column, for the scene's own panel and its camera's.
    private const string ScenePanel = "Scene";
    private const string CameraPanel = "Camera";

    private readonly string _path;
    private readonly StreamWriter _writer;
    private readonly TickRange[] _ticks;
    private readonly string[] _names;
    private readonly bool[] _matched;
    private readonly DebugPanel _panel = new();

    // The value each row of the stretch last wrote, the rows this tick has written or kept, and the names
    // of the entities it traced.
    private readonly Dictionary<TraceKey, string> _values = [];
    private readonly HashSet<TraceKey> _seen = [];
    private readonly HashSet<string> _present = new(StringComparer.Ordinal);

    // The rows the last traced tick held and the ones this tick holds, in the order they were reached.
    private List<TraceKey> _held = [];
    private List<TraceKey> _holding = [];

    // Tick 0 never follows it, and the first traced tick starts a stretch.
    private long _previousTick = -2;
    private Scene? _scene;

    // The acts a replay has applied for the tick about to be written: its subject and what it does.
    private readonly List<(long Tick, string Subject, string Act)> _acts = [];
    private long _rows;

    // Throws IOException or UnauthorizedAccessException when path cannot be written.
    internal RunTrace(string path, TickRange[] ticks, string[] names)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        _path = path;
        _writer = new StreamWriter(path, append: false);
        _writer.WriteLine(Header);
        _ticks = ticks;
        _names = names;
        _matched = new bool[names.Length];
    }

    // Holds an act for the tick its result shows, when the trace names that tick.
    internal void Act(long tick, string subject, string act)
    {
        if (Holds(tick))
        {
            _acts.Add((tick, subject, act));
        }
    }

    // Writes the rows of scenes' current scene when tick is one the trace names.
    internal void Write(SceneHost scenes, long tick)
    {
        if (!Holds(tick))
        {
            return;
        }

        Scene scene = scenes.Scene;

        // A tick that does not follow the last traced one, or is in another scene, starts a stretch, which names its
        // scene and writes in full.
        if (tick != _previousTick + 1 || !ReferenceEquals(scene, _scene))
        {
            _values.Clear();
            _held.Clear();
            WriteRow(tick, string.Empty, string.Empty, ScenePanel, scenes.SceneKey);
        }

        foreach ((long at, string subject, string act) in _acts)
        {
            if (at == tick)
            {
                WriteRow(tick, subject, string.Empty, "Act", act);
            }
        }

        _acts.RemoveAll(held => held.Tick <= tick);

        _previousTick = tick;
        _scene = scene;
        _seen.Clear();
        _present.Clear();
        _holding.Clear();
        if (Matches(ScenePanel))
        {
            _panel.Clear();
            scene.RunDebugPanel(_panel);
            WritePanel(tick, ScenePanel);
        }

        if (Matches(CameraPanel))
        {
            _panel.Clear();
            scene.Camera.RunDebugPanel(_panel);
            WritePanel(tick, CameraPanel);
        }

        foreach (Entity entity in scene.Entities)
        {
            if (Matches(entity))
            {
                WriteEntity(entity, tick);
            }
        }

        WriteGone(tick);
        (_held, _holding) = (_holding, _held);
    }

    // Warns of a name that matched nothing and of ticks past the run's end, and says what was written.
    // finalTick is the run's last tick with a scene, the one before its exiting step when it exited.
    internal void Finish(long finalTick)
    {
        // An exiting step leaves no scene to write, and its acts, such as the exit, are written alone.
        foreach ((long at, string subject, string act) in _acts)
        {
            WriteRow(at, subject, string.Empty, "Act", act);
        }

        _acts.Clear();
        for (int index = 0; index < _names.Length; index++)
        {
            if (!_matched[index])
            {
                Log.Warning($"--trace matched nothing named '{_names[index]}' at its ticks. A name matches an entity's Name, such as 'Avatar (1)', or its type name, and Scene and Camera match the scene's panel and its camera's.");
            }
        }

        long last = _ticks.Max(static range => range.IsOpen ? range.First : range.Last);
        if (last > finalTick)
        {
            Log.Warning(string.Create(CultureInfo.InvariantCulture, $"--trace names tick {last}, and the run ended at tick {finalTick}."));
        }

        Log.Info(string.Create(CultureInfo.InvariantCulture, $"--trace wrote {_rows} rows to {_path}"));
    }

    public void Dispose() => _writer.Dispose();

    private bool Holds(long tick)
    {
        foreach (TickRange range in _ticks)
        {
            if (range.Holds(tick))
            {
                return true;
            }
        }

        return false;
    }

    // Every name is tried, and each one that matches is marked.
    private bool Matches(Entity entity)
    {
        bool any = false;
        for (int index = 0; index < _names.Length; index++)
        {
            string name = _names[index];
            if (name == Everything
                || name == entity.Name
                || name == entity.GetType().Name)
            {
                _matched[index] = true;
                any = true;
            }
        }

        return any;
    }

    // Whether a name matches the scene's or the camera's panel, marking each that does.
    private bool Matches(string panel)
    {
        bool any = false;
        for (int index = 0; index < _names.Length; index++)
        {
            if (_names[index] == Everything || _names[index] == panel)
            {
                _matched[index] = true;
                any = true;
            }
        }

        return any;
    }

    private void WriteEntity(Entity entity, long tick)
    {
        _panel.Clear();
        entity.RunDebugPanel(_panel);
        WritePanel(tick, entity.Name);
    }

    // Writes the rows _panel holds under name. A command holds no value and writes no row. A section is numbered
    // among same-named ones before it, as two colliders are: BoxCollider2D, BoxCollider2D (1).
    private void WritePanel(long tick, string name)
    {
        _present.Add(name);
        string section = string.Empty;
        ReadOnlySpan<DebugPanelRow> rows = _panel.Rows;
        for (int index = 0; index < rows.Length; index++)
        {
            DebugPanelRow row = rows[index];
            switch (row.Kind)
            {
                case DebugPanelRowKind.Heading:
                    int before = 0;
                    foreach (DebugPanelRow earlier in rows[..index])
                    {
                        if (earlier.IsHeading && earlier.Label == row.Label)
                        {
                            before++;
                        }
                    }

                    section = before == 0 ? row.Label : string.Create(CultureInfo.InvariantCulture, $"{row.Label} ({before})");
                    break;

                case DebugPanelRowKind.Field:
                    Emit(tick, name, section, row.Label, row.Value!);
                    break;

                case DebugPanelRowKind.Toggle:
                    Emit(tick, name, section, row.Label, row.On ? bool.TrueString : bool.FalseString);
                    break;
            }
        }
    }

    // Writes the row when its value is new to the stretch or changed. A label a section repeats is
    // numbered as a repeated section is.
    private void Emit(long tick, string entity, string section, string field, string value)
    {
        TraceKey key = new(entity, section, field);
        for (int repeat = 1; !_seen.Add(key); repeat++)
        {
            key = key with { Field = string.Create(CultureInfo.InvariantCulture, $"{field} ({repeat})") };
        }

        _holding.Add(key);
        if (_values.TryGetValue(key, out string? last) && last == value)
        {
            return;
        }

        _values[key] = value;
        WriteRow(tick, entity, section, key.Field, value);
    }

    // The rows the last tick held and this one did not. An entity's rows are adjacent, and an entity gone
    // writes one row for all of them.
    private void WriteGone(long tick)
    {
        string? gone = null;
        foreach (TraceKey key in _held)
        {
            if (_seen.Contains(key))
            {
                continue;
            }

            _values.Remove(key);
            if (_present.Contains(key.Entity))
            {
                WriteRow(tick, key.Entity, key.Section, key.Field, string.Empty);
            }
            else if (key.Entity != gone)
            {
                gone = key.Entity;
                WriteRow(tick, key.Entity, string.Empty, string.Empty, string.Empty);
            }
        }
    }

    private void WriteRow(long tick, string entity, string section, string field, string value)
    {
        _writer.Write(tick.ToString(CultureInfo.InvariantCulture));
        _writer.Write(',');
        WriteCell(entity);
        _writer.Write(',');
        WriteCell(section);
        _writer.Write(',');
        WriteCell(field);
        _writer.Write(',');
        WriteCell(value);
        _writer.WriteLine();
        _rows++;
    }

    // Quoted as CSV quotes a cell holding a comma, a quote or a line break, which positions and rects do.
    private void WriteCell(string cell)
    {
        if (cell.AsSpan().IndexOfAny(",\"\r\n") < 0)
        {
            _writer.Write(cell);
            return;
        }

        _writer.Write('"');
        _writer.Write(cell.Replace("\"", "\"\"", StringComparison.Ordinal));
        _writer.Write('"');
    }

    // One row's place in a trace.
    private readonly record struct TraceKey(string Entity, string Section, string Field);
}

// The ticks from First to Last, both included. An open range runs to the run's end.
internal readonly record struct TickRange(long First, long Last)
{
    internal bool IsOpen => Last == long.MaxValue;

    internal bool Holds(long tick) => tick >= First && tick <= Last;

    // A tick (120), a range (100..160), an open range (100..), or a comma list of them, in whole ticks with
    // each range's first at or before its last. Null when text is none of those.
    internal static TickRange[]? TryParseList(string text)
    {
        string[] parts = text.Split(',');
        TickRange[] ranges = new TickRange[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            int dots = part.IndexOf("..", StringComparison.Ordinal);
            string first = dots < 0 ? part : part[..dots];
            string last = dots < 0 ? part : part[(dots + 2)..];
            long to = long.MaxValue;
            if (!long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out long from)
                || (last.Length > 0 && !long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out to))
                || from > to)
            {
                return null;
            }

            ranges[index] = new TickRange(from, to);
        }

        return ranges;
    }
}
