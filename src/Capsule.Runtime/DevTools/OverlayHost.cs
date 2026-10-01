using System.Diagnostics;
using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// The development overlay beside the game: the toggle, the input quarantine, the hold, and the page
// of rows it draws. The run is held while the overlay is open, so the page is rebuilt only after one
// of the host's own acts, never on an idle frame.
internal sealed class OverlayHost : IDisposable
{
    // The paces the Time Scale page offers, slowest first. 4x is the top because the default frame
    // budget is eight steps.
    internal static readonly (double Scale, string Label)[] TimeScales =
        [(0.25, "0.25x"), (0.5, "0.5x"), (1, "1x"), (2, "2x"), (4, "4x")];

    // Overlay frames a held key waits before repeating, and the frames between repeats.
    internal const int RepeatDelayFrames = 60;
    internal const int RepeatIntervalFrames = 10;

    private readonly FixedStepScheduler _scheduler;
    private readonly ISimulation _simulation;
    private readonly SceneHost? _scenes;
    private readonly IReadOnlyCollection<SceneRegistration> _registrations;

    // The overlay's own scene is never stepped. Its rows are written straight onto it.
    private readonly SceneSimulation _overlay;
    private readonly Run _overlayRun;
    private readonly InputState _input = new(OverlayActions.Bindings);

    private readonly DebugDrawBuffer _buffer = new();
    private readonly HashSet<string> _enabledChannels = new(StringComparer.Ordinal);
    private readonly DebugDrawRenderer _draws;

    // Null without a run of scenes.
    private readonly PanelRows? _panels;

    // The open pages, root first. The last one is drawn.
    private readonly List<Page> _pages = [new Page(PageKind.Root, null, 0, null)];

    private readonly List<OverlayRow> _rows = [];

    // The root's rows never change. They carry the hotkeys at any depth.
    private readonly List<OverlayRow> _rootRows = [];

    // The toggle first, then every button the overlay binds. Each is stripped from the game's
    // snapshot while the overlay is open, and after it closes until the button is released.
    private readonly InputButton[] _quarantine;
    private readonly bool[] _withheld;

    private readonly Func<long> _timestamp;

    private OverlayState _state;
    private bool _toggleDown;
    private bool _hideDown;
    private bool _framePaneOn;
    private string? _title;
    private int _focus;
    private int _first;
    private int _heldFrames;
    private bool _repeating;

    // Set by every host act that could change the page or the readout.
    private bool _pageStale = true;

    // The focus and window as last drawn. -1 forces the first open frame to draw.
    private int _shownFocus = -1;
    private int _shownFirst = -1;

    // Wheel notches not yet applied to the window. A touchpad reports fractions of a notch.
    private float _scrollRemainder;

    // The pointer takes the focus only by moving onto a row.
    private Vector2 _lastPointer;

    // Ticks stepped by hand run after the frame is sampled, so they count toward the next sample.
    private int _steppedTicks;

    // Observe stamps the frame's start. Negative while no frame is in progress.
    private long _observed = -1;
    private long _previousObserved = -1;

    // The hide press that showed the overlay is withheld from the rows until released, or the Hide
    // row would read it as a fresh press.
    private bool _hidePressConsumed;

    // The frame's device state as sampled for the rows, and with the overlay's buttons stripped for a
    // stepped tick.
    private DeviceSnapshot _sampled;
    private DeviceSnapshot _stripped;

    private double _lastFrameMs;
    private bool _exited;
    private bool _disposed;

    internal OverlayHost(
        InputButton button,
        FixedStepScheduler scheduler,
        ISimulation simulation,
        SceneHost? scenes = null,
        SceneRegistry? registry = null,
        Func<long>? timestamp = null)
    {
        _scheduler = scheduler;
        _simulation = simulation;
        _scenes = scenes;
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _registrations = registry?.Registrations ?? [];

        Scene = new OverlayScene(OverlayActions.KeyName(button));
        _draws = new DebugDrawRenderer(_buffer, _enabledChannels);
        Scene.Add(new DebugDrawEntity(_draws));
        _panels = scenes is null ? null : new PanelRows(scenes, activate => StepGame("Command", activate), Open);
        BuildRoot();

        _overlayRun = new Run
        {
            Canvas = Run.StandardCanvas,
            Sampling = TextureSampling.Point,
            EmitsDebugDraw = false,
        };

        // Withdrawn before the simulation writes its first frame.
        Scene.ShowMenu(false);
        _overlay = new SceneSimulation(Scene, run: _overlayRun);

        List<InputButton> quarantine = [button];
        foreach (InputAction action in OverlayActions.Actions)
        {
            quarantine.AddRange(OverlayActions.Bindings.ButtonsFor(action));
        }

        _quarantine = [.. quarantine];
        _withheld = new bool[_quarantine.Length];

        AttachBuffer();
    }

    private enum OverlayState
    {
        Closed,
        Open,
        Hidden,
    }

    private enum PageKind
    {
        Root,
        Scene,
        Entity,
        DebugDraw,
        TimeScale,
        LoadScene,
    }

    internal OverlayScene Scene { get; }

    // What the overlay draws, rewritten every frame it is on screen.
    internal FrameView View => _overlay.View;

    internal bool IsOpen => _state == OverlayState.Open;

    internal bool IsHidden => _state == OverlayState.Hidden;

    internal bool IsFramePaneOn => _framePaneOn;

    // The current page's rows as the last overlay frame built them.
    internal IReadOnlyList<OverlayRow> Rows => _rows;

    internal string? Title => _title;

    internal int Focus => _focus;

    internal int Depth => _pages.Count;

    internal string Status => Scene.Status;

    internal string Readout => Scene.Readout;

    // Raised on each edge of the hold: true as the overlay takes it, false as it lets go.
    internal Action<bool>? HoldChanged { get; set; }

    // Every channel a DebugDraw call has named since the buffer was attached, in reading order.
    internal string[] Channels
    {
        get
        {
            string[] channels = [.. _buffer.Channels];
            Array.Sort(channels, CompareLabels);

            return channels;
        }
    }

    // The pace in force: the run's on a run of scenes, where a game can set it, else the scheduler's.
    // A write applies at once.
    private double Pace
    {
        get => _scenes is { } scenes ? scenes.Run.TimeScale : _scheduler.TimeScale;
        set
        {
            if (_scenes is { } scenes)
            {
                scenes.Run.TimeScale = value;
            }

            _scheduler.TimeScale = value;
        }
    }

    internal bool IsChannelEnabled(string channel) => _enabledChannels.Contains(channel);

    // The overlay's integer scale on a back buffer of this height.
    internal static int ScaleFor(int height) => height < 1080 ? 1 : height < 2160 ? 2 : 3;

    // Called before the game's scheduler with the frame's device state, whose pointer is on the game's
    // canvas. Returns the snapshot the game sees. gameLayer and overlayScale carry the pointer onto
    // the overlay's canvas for the rows. A placement with no scale leaves it alone.
    internal DeviceSnapshot Observe(DeviceSnapshot snapshot, in ScreenPlacement gameLayer = default, int overlayScale = 1)
    {
        _observed = _timestamp();

        // The toggle's leading edge takes closed to open, open to closed and hidden to open.
        InputButton toggle = _quarantine[0];
        bool wasOpen = _state == OverlayState.Open;
        bool toggleDown = toggle.IsDown(snapshot);
        if (toggleDown && !_toggleDown)
        {
            _state = wasOpen ? OverlayState.Closed : OverlayState.Open;
            AttachBuffer();
            bool held = _state != OverlayState.Closed;
            if (held != _scheduler.Held)
            {
                _scheduler.Held = held;
                HoldChanged?.Invoke(held);
            }

            // The run stepped freely while the overlay was shut.
            _pageStale |= _state == OverlayState.Open;
        }

        _toggleDown = toggleDown;
        DeviceSnapshot sampled = toggle.IsNone ? snapshot : snapshot.Without(toggle);

        // Hidden rows read no input, so the edge that shows the overlay again is read here.
        bool hideDown = OverlayActions.Bindings.IsAnyDown(OverlayActions.Hide, snapshot);
        if (hideDown && !_hideDown && _state == OverlayState.Hidden)
        {
            _state = OverlayState.Open;
            _hidePressConsumed = true;
            _pageStale = true;
        }

        _hideDown = hideDown;
        _hidePressConsumed &= hideDown;
        if (_hidePressConsumed)
        {
            foreach (InputButton hide in OverlayActions.Bindings.ButtonsFor(OverlayActions.Hide))
            {
                sampled = sampled.Without(hide);
            }
        }

        if (gameLayer.Scale > 0f && overlayScale > 0)
        {
            Vector2 window = (sampled.Pointer * gameLayer.Scale) + gameLayer.Origin;
            sampled = sampled.WithPointer(window / overlayScale);
        }

        _sampled = sampled;

        // A press that served the overlay cannot land on the resumed step.
        bool open = wasOpen || _state == OverlayState.Open;
        for (int index = 0; index < _quarantine.Length; index++)
        {
            if (open || _withheld[index])
            {
                InputButton button = _quarantine[index];
                _withheld[index] = button.IsDown(snapshot);
                snapshot = snapshot.Without(button);
            }
        }

        if (open)
        {
            snapshot = snapshot.WithScroll(Vector2.Zero);
        }

        _stripped = snapshot;

        return snapshot;
    }

    // Called after the game's scheduler. Open, the frame's input is read onto the rows. Closed, the
    // frame is rewritten without input, so the draws and the pane follow the game's ticks. renderer
    // places the overlay on the back buffer and carries the game frame's cost. A host with no renderer
    // supplies that cost as lastFrameMs.
    internal void Step(FrameRenderer? renderer = null, double lastFrameMs = 0)
    {
        long stepped = _timestamp();

        SettleDraws();

        if (_exited)
        {
            return;
        }

        _lastFrameMs = lastFrameMs;
        if (renderer is not null)
        {
            (int Width, int Height) backBuffer = renderer.BackBufferSize;
            Refit(backBuffer);

            // Font pixels into world units, which keeps a label at the overlay's glyph size at any
            // zoom. One until a frame has drawn a world.
            float pixelsPerUnit = renderer.WorldPixelsPerUnit;
            _draws.TextScale = pixelsPerUnit > 0f ? ScaleFor(backBuffer.Height) / pixelsPerUnit : 1f;
            _lastFrameMs = renderer.LastFrameMs;
        }

        SampleFrame(stepped);
        _draws.Alpha = _scheduler.InterpolationAlpha;

        bool open = _state == OverlayState.Open;
        bool menuChanged = Scene.ShowMenu(open);
        if (open)
        {
            _input.Advance(in _sampled);
            ReadRows();

            if (_pageStale)
            {
                RefreshReadout();
            }

            // An act may have hidden or closed the overlay from inside its own frame.
            if (_state == OverlayState.Open)
            {
                if (BuildRows() || _focus != _shownFocus || _first != _shownFirst)
                {
                    Scene.Show(_title, _rows, _focus, _first);
                    _shownFocus = _focus;
                    _shownFirst = _first;
                }
            }
            else
            {
                Scene.ShowMenu(false);
                _scrollRemainder = 0f;
            }

            _pageStale = false;
            _overlay.RewriteView();
        }
        else if (menuChanged || _enabledChannels.Count > 0 || _framePaneOn)
        {
            _scrollRemainder = 0f;
            _overlay.RewriteView();
        }
    }

    internal void Draw(FrameRenderer renderer)
    {
        if (_exited || (_state == OverlayState.Closed && !_framePaneOn && _enabledChannels.Count == 0))
        {
            return;
        }

        renderer.DrawOverlay(_overlay.View, ScaleFor(renderer.BackBufferSize.Height));
    }

    // Flips the pane for the rest of the play session.
    internal void ToggleFramePane()
    {
        _framePaneOn = !_framePaneOn;
        if (_framePaneOn)
        {
            Scene.Pane.Reset();
        }

        Scene.ShowFramePane(_framePaneOn);
        _pageStale = true;
    }

    // Flips a channel for the rest of the play session. The held scene is asked to emit as it stands,
    // so the toggle shows on the next frame without a step.
    internal void ToggleChannel(string channel)
    {
        if (!_enabledChannels.Remove(channel))
        {
            _enabledChannels.Add(channel);
        }

        AttachBuffer();
        EmitDraws();
        _pageStale = true;
    }

    internal void Load(in SceneTransition transition) => Request("Load", in transition);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DebugDraw.UseBuffer(null);
        _overlay.Dispose();
    }

    // Reads this frame's input onto the rows: back, the focus, the row chosen by key or pointer, and
    // the hotkeys.
    private void ReadRows()
    {
        BuildRows();

        bool held = _input.IsHeld(OverlayActions.MenuUp) || _input.IsHeld(OverlayActions.MenuDown);
        bool pressed = _input.WasPressed(OverlayActions.MenuUp) || _input.WasPressed(OverlayActions.MenuDown);
        foreach (OverlayRow row in _rootRows)
        {
            if (row is { Repeats: true, Hotkey: { } hotkey })
            {
                held |= _input.IsHeld(hotkey);
                pressed |= _input.WasPressed(hotkey);
            }
        }

        _heldFrames = held && !pressed ? _heldFrames + 1 : 0;
        _repeating = _heldFrames >= RepeatDelayFrames
            && (_heldFrames - RepeatDelayFrames) % RepeatIntervalFrames == 0;

        if (_input.WasPressed(OverlayActions.Back))
        {
            Pop();
            BuildRows();
        }

        if (Pressed(OverlayActions.MenuDown))
        {
            Move(1);
        }

        if (Pressed(OverlayActions.MenuUp))
        {
            Move(-1);
        }

        Scroll(_input.Axis(OverlayActions.Scroll));

        bool pointerMoved = _sampled.Pointer != _lastPointer;
        _lastPointer = _sampled.Pointer;

        int hovered = Scene.RowAt(_sampled.Pointer);
        if (hovered >= 0 && hovered < _rows.Count && _rows[hovered].Activate is not null)
        {
            bool clicked = _input.WasPressed(OverlayActions.Click);
            if (pointerMoved || clicked)
            {
                _focus = hovered;
            }

            if (clicked)
            {
                Activate(_rows[hovered]);
            }
        }

        if (_input.WasPressed(OverlayActions.Confirm) && _focus < _rows.Count)
        {
            Activate(_rows[_focus]);
        }

        bool atRoot = _pages.Count == 1;
        foreach (OverlayRow row in _rootRows)
        {
            if (row.Hotkey is { } hotkey && (atRoot || !row.OpensMenu) && Pressed(hotkey, row.Repeats))
            {
                Activate(row);
            }
        }
    }

    // True on the press edge, and again every RepeatIntervalFrames while held past the delay.
    private bool Pressed(InputAction action, bool repeats = true) =>
        _input.WasPressed(action) || (repeats && _repeating && _input.IsHeld(action));

    private void Activate(in OverlayRow row)
    {
        if (row.Activate is { } activate)
        {
            Scene.SetStatus(string.Empty);
            activate();
        }
    }

    // Moves the focus by step over the interactive rows, wrapping at either end, then scrolls the
    // window the least that shows it.
    private void Move(int step)
    {
        for (int moved = 1; moved <= _rows.Count; moved++)
        {
            int index = ((_focus + (step * moved)) % _rows.Count + _rows.Count) % _rows.Count;
            if (_rows[index].Activate is not null)
            {
                _focus = index;
                _first = Math.Clamp(_first, _focus - OverlayScene.MaxRows + 1, _focus);

                return;
            }
        }
    }

    // Moves the window three rows per notch, up for a positive notch, and leaves the focus alone. The
    // fraction of a row carries to the next frame.
    private void Scroll(float notches)
    {
        if (notches == 0f)
        {
            return;
        }

        _scrollRemainder -= notches * 3f;
        int rows = (int)MathF.Truncate(_scrollRemainder);
        _scrollRemainder -= rows;

        _first = Math.Clamp(_first + rows, 0, Math.Max(0, _rows.Count - OverlayScene.MaxRows));
    }

    // Rebuilds a stale page from the run, popping entity panels whose subject has left the scene.
    // Returns whether it rebuilt.
    private bool BuildRows()
    {
        if (!_pageStale)
        {
            return false;
        }

        while (_pages[^1] is { Kind: PageKind.Entity } departed && !_panels!.Holds(departed.Subject!))
        {
            // The name it had when opened. An entity out of the scene has no place among its siblings.
            Scene.SetStatus($"{departed.Name} left the scene");
            Pop();
        }

        _rows.Clear();
        Page page = _pages[^1];
        if (page.Kind == PageKind.Root)
        {
            _rows.AddRange(_rootRows);
        }

        _title = page.Kind switch
        {
            PageKind.Root => null,
            PageKind.Scene => _panels!.ScenePage(_rows),
            PageKind.Entity => _panels!.EntityPanel(page.Subject!, _rows),
            PageKind.DebugDraw => BuildDebugDraw(),
            PageKind.TimeScale => BuildTimeScale(),
            _ => BuildLoadScene(),
        };

        _focus = Nearest(Math.Clamp(_focus, 0, Math.Max(0, _rows.Count - 1)));

        // Clamped to the page, not to the focus. The wheel moves the window away from the focus.
        _first = Math.Clamp(_first, 0, Math.Max(0, _rows.Count - OverlayScene.MaxRows));

        return true;
    }

    // The interactive row nearest index, preferring the earlier at a tie. Returns index where the
    // page has none.
    private int Nearest(int index)
    {
        for (int distance = 0; distance < _rows.Count; distance++)
        {
            if (index - distance >= 0 && _rows[index - distance].Activate is not null)
            {
                return index - distance;
            }

            if (index + distance < _rows.Count && _rows[index + distance].Activate is not null)
            {
                return index + distance;
            }
        }

        return index;
    }

    private void BuildRoot()
    {
        bool scenes = _scenes is not null;
        if (scenes)
        {
            _rootRows.Add(new OverlayRow("Scene", () => Open(PageKind.Scene), OverlayActions.ScenePage, OpensMenu: true));
        }

        _rootRows.Add(new OverlayRow("Step", StepGame, OverlayActions.Step, Repeats: true));
        _rootRows.Add(new OverlayRow("Debug Draw", OpenDebugDraw, OverlayActions.DebugDraw, OpensMenu: true));
        _rootRows.Add(new OverlayRow("Time Scale", () => Open(PageKind.TimeScale), OverlayActions.TimeScale, OpensMenu: true));

        if (scenes)
        {
            _rootRows.Add(new OverlayRow("Restart", () => Request("Restart", SceneTransition.Restart(null, false)), OverlayActions.Restart));

            if (_registrations.Count > 0)
            {
                _rootRows.Add(new OverlayRow("Load Scene", () => Open(PageKind.LoadScene), OverlayActions.LoadScene, OpensMenu: true));
            }
        }

        _rootRows.Add(new OverlayRow("Frame Pane", ToggleFramePane, OverlayActions.FramePane));
        _rootRows.Add(new OverlayRow("Hide", () => _state = OverlayState.Hidden, OverlayActions.Hide));

        if (scenes)
        {
            _rootRows.Add(new OverlayRow("Exit", Exit, OverlayActions.Exit));
        }
    }

    private string BuildDebugDraw()
    {
        foreach (string channel in Channels)
        {
            string label = (IsChannelEnabled(channel) ? "[x] " : "[ ] ") + channel;
            _rows.Add(new OverlayRow(label, () => ToggleChannel(channel)));
        }

        if (_rows.Count == 0)
        {
            _rows.Add(new OverlayRow("<No channel has emitted yet>", null));
        }

        return "Debug Draw";
    }

    // No row is marked when a game set a pace off the ladder.
    private string BuildTimeScale()
    {
        foreach ((double scale, string label) in TimeScales)
        {
            _rows.Add(new OverlayRow((Pace == scale ? "(x) " : "( ) ") + label, () => SetTimeScale(scale)));
        }

        return "Time Scale";
    }

    // Case-insensitive, then ordinal. A scene class "Room" and an unclaimed document "room" differ
    // only by case, and List<T>.Sort is unstable.
    private static int CompareLabels(string a, string b)
    {
        int result = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

        return result != 0 ? result : string.CompareOrdinal(a, b);
    }

    // Every registration by its name: a class by its class name, a document-only registration by its
    // document's key.
    private string BuildLoadScene()
    {
        List<(string Label, SceneTransition Target)> entries = [];

        foreach (SceneRegistration registration in _registrations)
        {
            SceneTransition target = registration.DocumentName is { } name
                ? SceneTransition.ToName(name, null)
                : SceneTransition.ToScene(registration.SceneType!, null);
            entries.Add((registration.Name, target));
        }

        entries.Sort(static (a, b) => CompareLabels(a.Label, b.Label));

        foreach ((string label, SceneTransition target) in entries)
        {
            _rows.Add(new OverlayRow(label, () => Load(in target)));
        }

        return "Load Scene";
    }

    // The overlay never resets the pace, and no tick is stepped.
    private void SetTimeScale(double scale)
    {
        Pace = scale;
        _pageStale = true;
    }

    // The scene emits first, so a run held before its first step still lists its channels.
    private void OpenDebugDraw()
    {
        EmitDraws();
        Open(PageKind.DebugDraw);
    }

    private void Open(PageKind kind) => Open(new Page(kind, null, _focus, null));

    private void Open(Entity subject) =>
        Open(new Page(PageKind.Entity, subject, _focus, PanelRows.Label(subject)));

    private void Open(Page page)
    {
        _pages.Add(page);
        _focus = 0;
        _first = 0;
        _scrollRemainder = 0f;
        _pageStale = true;
    }

    // Returns to the page beneath, focused on the row that opened this one. Does nothing at the root.
    private void Pop()
    {
        if (_pages.Count < 2)
        {
            return;
        }

        _focus = _pages[^1].ReturnFocus;
        _first = 0;
        _scrollRemainder = 0f;
        _pages.RemoveAt(_pages.Count - 1);
        _pageStale = true;
    }

    private void Exit()
    {
        _scenes!.Run.RequestExit();
        StepGame();
    }

    // A request the run declines because a transition is pending is shown and not stepped, or the
    // pending transition would be stepped in its place. A refused request is shown and logged. Either
    // way the run stays held on its current scene.
    private void Request(string action, in SceneTransition transition)
    {
        _pageStale = true;
        try
        {
            if (!_scenes!.Run.TryRequest(in transition))
            {
                Scene.SetStatus($"{action} refused: a transition is already pending");

                return;
            }
        }
        catch (Exception failure)
        {
            Report(action, failure);

            return;
        }

        StepGame(action, null);
    }

    // An incoming scene that fails to come up is shown and logged, and the run stays on its current
    // scene. Any other failure of the tick propagates, as it does from the Step row.
    private void StepGame(string action, Action? before)
    {
        try
        {
            StepGame(before);
        }
        catch (Exception failure) when (_scenes!.TransitionFailed)
        {
            Report(action, failure);
        }
    }

    private void StepGame() => StepGame(null);

    // before is a host act that runs inside the step, ahead of the scene's own work.
    private void StepGame(Action? before)
    {
        _exited = _scheduler.StepOnce(in _stripped, _simulation, before);
        _steppedTicks += _scheduler.StepsThisFrame;
        SettleDraws();
        _pageStale = true;
    }

    private void Report(string action, Exception failure)
    {
        Log.Error($"{action} from the debug menu failed: {failure}");
        Scene.SetStatus($"{action} failed: {failure.GetType().Name}: {failure.Message}");
    }

    // Closes the frame Observe opened and hands the pane its sample. The first frame has no interval
    // and is not sampled. A Step with no Observe before it is not a frame.
    private void SampleFrame(long stepped)
    {
        long observed = _observed;
        if (observed < 0)
        {
            return;
        }

        _observed = -1;
        long previous = _previousObserved;
        _previousObserved = observed;
        int steps = _scheduler.StepsThisFrame + _steppedTicks;
        _steppedTicks = 0;
        if (!_framePaneOn || previous < 0)
        {
            return;
        }

        Scene.Pane.Push(new FrameSample(
            Milliseconds(observed - previous),
            Milliseconds(stepped - observed),
            _lastFrameMs,
            steps));
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    // The overlay's canvas is the back buffer at its own integer scale.
    private void Refit((int Width, int Height) backBuffer)
    {
        (int width, int height) = backBuffer;
        if (width > 0 && height > 0)
        {
            int scale = ScaleFor(height);
            _overlayRun.Canvas = new Vector2(width / (float)scale, height / (float)scale);
        }
    }

    // The held scene's debug pass, run when the settled step has not already drawn into the buffer.
    // The buffer is settled at that step's tick first, so the draws expire with the next step.
    private void EmitDraws()
    {
        long tick = _scheduler.Tick;
        if (!DebugDraw.IsAttached || _scenes is not { } scenes || _buffer.EmittedTick >= tick - 1)
        {
            return;
        }

        _buffer.Settle(tick - 1);
        scenes.EmitDebugDraws();
        SettleDraws();
    }

    private void RefreshReadout() =>
        Scene.SetReadout(_scenes is { } scenes ? scenes.Scene.GetType().Name : string.Empty, _scheduler.Tick);

    // A frame that ran several steps settles once at its last tick, so draws from its later steps
    // leave up to that many ticks early.
    private void SettleDraws() => _buffer.Settle(_scheduler.Tick);

    // The buffer is attached only while what it holds can be seen. The Debug Draw page therefore lists
    // the channels that emitted while attached, not every channel since boot.
    private void AttachBuffer() =>
        DebugDraw.UseBuffer(_state != OverlayState.Closed || _enabledChannels.Count > 0 ? _buffer : null);

    // One open page. An entity panel carries its subject and the name it had when opened. ReturnFocus
    // is the row that opened it.
    private readonly record struct Page(PageKind Kind, Entity? Subject, int ReturnFocus, string? Name);
}
