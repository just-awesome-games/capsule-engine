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
// of rows it draws. The overlay holds the run while open, so its page can change only through the
// host's own acts; the current page's rows, the root's hotkey rows and the readout are rebuilt from
// the run only when an act since the last rebuild could have changed them, never on an idle frame.
internal sealed class OverlayHost : IDisposable
{
    // The paces the Time Scale page offers, slowest first. 4x is the top, because the default frame
    // budget is eight steps and past it a frame drops the backlog it cannot run.
    internal static readonly (double Scale, string Label)[] TimeScales =
        [(0.25, "0.25x"), (0.5, "0.5x"), (1, "1x"), (2, "2x"), (4, "4x")];

    // Overlay frames a held key waits before repeating, and the frames between repeats.
    internal const int RepeatDelayFrames = 60;
    internal const int RepeatIntervalFrames = 10;

    private readonly FixedStepScheduler _scheduler;
    private readonly ISimulation _simulation;
    private readonly SceneHost? _scenes;

    // The overlay's own scene and the run it is drawn under. The scene is never stepped. Its rows are
    // written straight onto it and the view is rewritten from there.
    private readonly SceneSimulation _overlay;
    private readonly Run _overlayRun;
    private readonly InputState _input = new(OverlayActions.Bindings);

    // Where the game's DebugDraw calls land while this overlay is attached, the channels switched on,
    // and the renderer that reads both onto the scene's world.
    private readonly DebugDrawBuffer _buffer = new();
    private readonly HashSet<string> _enabledChannels = new(StringComparer.Ordinal);
    private readonly DebugDrawRenderer _draws;

    // The scene page and entity panels over the held scene. Null without a run of scenes.
    private readonly PanelRows? _panels;

    // The open pages, root first. The last one is drawn.
    private readonly List<Page> _pages = [new Page(PageKind.Root, null, 0, null)];

    // The current page's rows and the root's, which carry the hotkeys at any depth.
    private readonly List<OverlayRow> _rows = [];
    private readonly List<OverlayRow> _rootRows = [];

    // The toggle first, then every button the overlay binds. Each is stripped from the game's
    // snapshot while the overlay is open, and after it closes until the button is released.
    private readonly InputButton[] _quarantine;
    private readonly bool[] _withheld;

    private OverlayState _state;
    private bool _toggleDown;
    private bool _hideDown;
    private bool _framePaneOn;
    private string? _title;
    private int _focus;
    private int _first;
    private int _heldFrames;
    private bool _repeating;

    // Set by every host act that could change the current page, the root's hotkey rows or the
    // readout, and cleared once each open frame after everything that reads it this frame has.
    // An idle frame, with nothing set it, rebuilds nothing.
    private bool _pageStale = true;

    // Whether _rootRows still matches the run. Paired with _pageStale rather than folded into it:
    // the current page is rebuilt by the one place that reads _pageStale for it, but HotkeyRows is
    // reached at whatever depth the frame is at when a hotkey is checked, which is depth one (no
    // _rootRows involved at all) on the very frame a submenu is pushed. A cache keyed on _pageStale
    // alone would then find the flag already spent by the current page's own rebuild and never fill
    // _rootRows for the submenu that just opened. Both bits go stale together; only this one is
    // consumed at its own point of use.
    private bool _rootRowsValid;

    // The focus and window as Scene.Show last drew them, so a frame that neither rebuilt the page
    // nor moved either one skips the draw. -1 forces the first open frame to draw regardless.
    private int _shownFocus = -1;
    private int _shownFirst = -1;

    // Wheel notches not yet applied to the window: a fine wheel or a touchpad reports fractions of a
    // notch, which add up here until they make a whole one.
    private float _scrollRemainder;

    // Where the pointer sat last frame, so it only takes the focus by moving onto a row, not by
    // resting on one the keys just moved off of.
    private Vector2 _lastPointer;

    // Ticks the overlay stepped by hand since the last sample. They run after the frame is sampled and
    // the next advance clears the scheduler's count, so they are counted on the frame that follows.
    private int _steppedTicks;

    // The frame's clock. Observe stamps the start, and Step reads the update bracket and the interval
    // from the previous frame's start. Negative while no frame is in progress.
    private readonly Func<long> _timestamp;
    private long _observed = -1;
    private long _previousObserved = -1;

    // Set by the hide press that showed the overlay and cleared on its release. That press is withheld
    // from the rows, or they would read it as a fresh press and hide again.
    private bool _hidePressConsumed;

    // The frame's device state as sampled for the rows, and with their buttons stripped for a stepped
    // tick.
    private DeviceSnapshot _sampled;
    private DeviceSnapshot _stripped;

    private RenderStats _lastFrame;
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
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(simulation);

        _scheduler = scheduler;
        _simulation = simulation;
        _scenes = scenes;
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        Registrations = registry is { } registered ? registered.Registrations : [];

        Scene = new OverlayScene(OverlayActions.KeyName(button));
        _draws = new DebugDrawRenderer(_buffer, _enabledChannels);
        Scene.Add(new DebugDrawEntity(_draws));
        _panels = scenes is { } held ? new PanelRows(held, activate => StepGame("Command", activate), Open) : null;

        _overlayRun = new Run
        {
            Canvas = Run.StandardCanvas,
            Sampling = TextureSampling.Point,
            EmitsDebugDraw = false,
        };

        // Withdrawn before the simulation writes its first frame, which stands until a frame with the
        // overlay open rewrites it.
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

    // Which page is drawn. An entity panel carries its subject.
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

    internal bool HasScenes => _scenes is not null;

    internal IReadOnlyCollection<SceneRegistration> Registrations { get; }

    internal bool IsOpen => _state == OverlayState.Open;

    internal bool IsHidden => _state == OverlayState.Hidden;

    internal bool IsFramePaneOn => _framePaneOn;

    // What the current page holds, as the last overlay frame built it.
    internal IReadOnlyList<OverlayRow> Rows => _rows;

    internal string? Title => _title;

    internal int Focus => _focus;

    internal int Depth => _pages.Count;

    internal string Status => Scene.Status;

    // Raised on each edge of the hold, true as the overlay takes it and false as it lets go, for host
    // presentation that follows the simulation's standstill.
    internal Action<bool>? HoldChanged { get; set; }

    // Every channel a DebugDraw call has named since the overlay was attached, sorted.
    internal string[] Channels
    {
        get
        {
            string[] channels = [.. _buffer.Channels];
            Array.Sort(channels, StringComparer.Ordinal);

            return channels;
        }
    }

    internal bool IsChannelEnabled(string channel) => _enabledChannels.Contains(channel);

    internal string Readout
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            RefreshReadout();

            return Scene.Readout;
        }
    }

    // The scale the overlay is drawn at on a back buffer of this height, so its text is legible at
    // any window size.
    internal static int ScaleFor(int height) => height < 1080 ? 1 : height < 2160 ? 2 : 3;

    // The host calls this before the game's scheduler with the frame's device state, whose pointer is
    // already on the game's canvas, and hands the game what it returns. gameLayer and overlayScale
    // carry that pointer onto the overlay's canvas, and a placement with no scale leaves it alone. The
    // game's snapshot keeps its own pointer.
    internal DeviceSnapshot Observe(DeviceSnapshot snapshot, in ScreenPlacement gameLayer = default, int overlayScale = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _observed = _timestamp();

        // The toggle's leading edge, read on the raw state, takes closed to open, open to closed and
        // hidden back to open. The toggle is withheld from the rows, and a toggle that is also a
        // hotkey does not fire that key's action on the frame it opens.
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

            // Entering Open from Closed or Hidden: the run stepped freely while this was shut, so
            // the page it shows next may be stale even though nothing here changed it.
            if (_state == OverlayState.Open && !wasOpen)
            {
                SetStale();
            }
        }

        _toggleDown = toggleDown;
        DeviceSnapshot sampled = toggle.IsNone ? snapshot : snapshot.Without(toggle);

        // The overlay is not read while hidden, so the edge that shows it again is read here, and
        // that press is withheld from the rows until released.
        bool hideDown = OverlayActions.Bindings.IsAnyDown(OverlayActions.Hide, snapshot);
        if (hideDown && !_hideDown && _state == OverlayState.Hidden)
        {
            _state = OverlayState.Open;
            _hidePressConsumed = true;
            SetStale();
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

        // Every listed button is withheld from the game while the overlay is open, and after it closes
        // until that button is released. A press that served the overlay cannot land on the resumed
        // step.
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

    // The host calls this after the game's scheduler. The game is held while the overlay is open, so
    // this reads the frame's input onto the rows and rewrites them. While closed, the frame is
    // rewritten without input, so the draws and the pane still follow the game's ticks. renderer
    // places the overlay on the back buffer and carries the game frame's cost. A host with no
    // renderer supplies that cost as lastFrame.
    internal void Step(FrameRenderer? renderer = null, RenderStats lastFrame = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The update bracket ends here, ahead of the overlay's own work.
        long stepped = _timestamp();

        SettleDraws();

        if (_exited)
        {
            return;
        }

        _lastFrame = lastFrame;
        if (renderer is not null)
        {
            (int Width, int Height) backBuffer = renderer.BackBufferSize;
            Refit(backBuffer);

            // Font pixels into world units at the last frame's placement, which keeps a label at the
            // overlay's glyph size at any zoom. One until a frame has drawn a world.
            float pixelsPerUnit = renderer.WorldPixelsPerUnit;
            _draws.TextScale = pixelsPerUnit > 0f ? ScaleFor(backBuffer.Height) / pixelsPerUnit : 1f;
            _lastFrame = renderer.LastFrame;
        }

        SampleFrame(stepped);

        // The frame's alpha, which the game frame is about to be drawn at. One while held.
        _draws.Alpha = _scheduler.InterpolationAlpha;

        bool open = _state == OverlayState.Open;
        bool menuChanged = Scene.ShowMenu(open);
        if (menuChanged && !open)
        {
            _scrollRemainder = 0f;
        }

        if (open)
        {
            _input.Advance(in _sampled);
            ReadRows();

            // The readout changes only on a step or a transition, both of which set _pageStale, so
            // an idle frame between them reads it here and does nothing.
            if (_pageStale)
            {
                RefreshReadout();
            }

            if (_state == OverlayState.Open)
            {
                // The rows are rebuilt after the input as well as before it, because an act may have
                // stepped the run, changed the scene or opened a page. The frame that did so shows
                // the result. An idle frame rebuilds nothing, and draws again only if the pointer or
                // the keys moved the focus or the window since the last draw.
                bool rebuilt = BuildRows();
                if (rebuilt || _focus != _shownFocus || _first != _shownFirst)
                {
                    Scene.Show(_title, _rows, _focus, _first);
                    _shownFocus = _focus;
                    _shownFirst = _first;
                }
            }
            else
            {
                // Hidden or closed from inside its own frame, so the panel leaves before this frame
                // is drawn.
                Scene.ShowMenu(false);
                _scrollRemainder = 0f;
            }

            // Every consumer of this frame's staleness (RefreshReadout, BuildRows) has now run, so
            // the current page goes stale again only on the next act. HotkeyRows keeps _rootRows
            // valid on its own, since it is reached at whatever depth a hotkey is checked at.
            _pageStale = false;

            _overlay.RewriteView();
        }
        else if (menuChanged || _enabledChannels.Count > 0 || _framePaneOn)
        {
            _overlay.RewriteView();
        }
    }

    internal void Draw(FrameRenderer renderer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderer);

        // Nothing of the overlay is on screen, so the frame it would submit holds nothing. Step has
        // already refitted this back buffer.
        if (_exited || (_state == OverlayState.Closed && !_framePaneOn && _enabledChannels.Count == 0))
        {
            return;
        }

        (int _, int height) = renderer.BackBufferSize;
        renderer.DrawOverlay(_overlay.View, ScaleFor(height));
    }

    // Flips the pane for the rest of the play session. It draws on the overlay's next frame, open,
    // closed or hidden.
    internal void ToggleFramePane()
    {
        _framePaneOn = !_framePaneOn;
        if (_framePaneOn)
        {
            Scene.Pane.Reset();
        }

        Scene.ShowFramePane(_framePaneOn);
        SetStale();
    }

    // Flips a channel for the rest of the play session. Draws follow on the overlay's next frame
    // whether or not the game steps, because the scene is asked to emit as it stands. A run held on
    // its settled step shows the toggle immediately.
    internal void ToggleChannel(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (!_enabledChannels.Remove(channel))
        {
            _enabledChannels.Add(channel);
        }

        AttachBuffer();
        EmitDraws();
        SetStale();
    }

    // Whether scale is the pace in force. A pace a game set off the ladder matches no row.
    internal bool IsTimeScale(double scale) => Pace == scale;

    // Sets the pace for the rest of the run. The overlay never resets it, the simulation is unchanged,
    // and no tick is stepped.
    internal void SetTimeScale(double scale)
    {
        Pace = scale;
        SetStale();
    }

    internal void Hide() => _state = OverlayState.Hidden;

    internal void Restart() => Request("Restart", SceneTransition.Restart(null, false));

    internal void Load(in SceneTransition transition) => Request("Load", in transition);

    internal void Exit()
    {
        GameRun.RequestExit();
        StepGame();
    }

    // A stepped tick is an ordinary tick. A game that throws inside one crashes as usual.
    internal void StepGame() => StepGame(null);

    void IDisposable.Dispose() => Dispose();

    internal void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DebugDraw.UseBuffer(null);
        _overlay.Dispose();
    }

    // The pace in force: the run's on a run of scenes, where a value a game set marks its ladder row
    // and a ladder pick is visible to the game. Without a run to hold it, the scheduler's own. A write
    // applies at once and does not wait for the host's next copy.
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

    private SceneHost GameHost =>
        _scenes ?? throw new InvalidOperationException("The overlay's scene actions need a run of scenes.");

    private Run GameRun => GameHost.Run;

    // Reads this frame's input onto the rows: the page beneath, the focus, the row chosen by key or
    // pointer, and the hotkeys.
    private void ReadRows()
    {
        BuildRows();

        bool held = _input.IsHeld(OverlayActions.MenuUp) || _input.IsHeld(OverlayActions.MenuDown);
        bool pressed = _input.WasPressed(OverlayActions.MenuUp) || _input.WasPressed(OverlayActions.MenuDown);
        foreach (OverlayRow row in HotkeyRows())
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

        // The pointer takes the focus by moving onto a row or by a click landing on one; resting
        // still, the keys own the focus and are not overwritten by the row the pointer already sat on.
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

        // The root's rows carry the hotkeys at any depth, so the run can be stepped from an entity
        // panel. An opener's hotkey fires only at the root, and no page is stacked from inside one.
        bool atRoot = _pages.Count == 1;
        List<OverlayRow> hotkeys = HotkeyRows();
        for (int index = 0; index < hotkeys.Count; index++)
        {
            OverlayRow row = hotkeys[index];
            if (row.Hotkey is { } hotkey && (atRoot || !row.OpensMenu) && Pressed(hotkey, row.Repeats))
            {
                Activate(row);
            }
        }
    }

    // The root's rows, which are the current ones at the root and a cached list below it, rebuilt
    // whenever the same act that stales the current page has also invalidated the cache.
    private List<OverlayRow> HotkeyRows()
    {
        if (_pages.Count == 1)
        {
            return _rows;
        }

        if (!_rootRowsValid)
        {
            _rootRows.Clear();
            BuildRoot(_rootRows);
            _rootRowsValid = true;
        }

        return _rootRows;
    }

    // Marks the current page, the readout and the root's hotkey rows stale: called by every host act
    // that could change what any of them show. A rebuild each then runs at its own point of use on
    // the frame that follows, and not before.
    private void SetStale()
    {
        _pageStale = true;
        _rootRowsValid = false;
    }

    // True on the press edge, and again every RepeatIntervalFrames while the key is held past the
    // delay. Directions always repeat. A row repeats where it says so.
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

    // Moves the focus by step over the interactive rows, wrapping at either end, then brings the
    // window to it: the least the wheel left it that still shows the new focus.
    private void Move(int step)
    {
        if (_rows.Count == 0)
        {
            return;
        }

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

    // Moves the window three rows per whole notch turned, up for a positive notch (away from the
    // user) and down for a negative one, as the pre-audit overlay did; the focus stays where it is.
    // The fraction of a notch that does not make a whole one carries to the next frame.
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

    // Rebuilds the current page's rows from the run as it stands when a host act has made them
    // stale, dropping an entity panel whose subject has left the scene, and brings the focus and
    // the window to them. Returns at once, doing nothing, on a frame nothing set stale. Returns
    // whether it rebuilt.
    private bool BuildRows()
    {
        if (!_pageStale)
        {
            return false;
        }

        while (true)
        {
            _rows.Clear();
            Page page = _pages[^1];

            if (page.Kind == PageKind.Entity && _panels is { } panels)
            {
                if (panels.Holds(page.Subject!))
                {
                    _title = panels.EntityPanel(page.Subject!, _rows);
                    break;
                }

                // Named as it was when its panel was opened, because an entity out of the scene has
                // no readable place among its siblings.
                Scene.SetStatus($"{page.Name} left the scene");
                Pop();

                continue;
            }

            _title = page.Kind switch
            {
                PageKind.Root => BuildRoot(_rows),
                PageKind.Scene => _panels!.ScenePage(_rows),
                PageKind.DebugDraw => BuildDebugDraw(_rows),
                PageKind.TimeScale => BuildTimeScale(_rows),
                _ => BuildLoadScene(_rows),
            };

            break;
        }

        _focus = Nearest(Math.Clamp(_focus, 0, Math.Max(0, _rows.Count - 1)));

        // Clamped to the page alone, not to the focus: the wheel moves this away from the focus, and
        // Move is what brings it back once a direction press changes which row is focused.
        _first = Math.Clamp(_first, 0, Math.Max(0, _rows.Count - OverlayScene.MaxRows));

        return true;
    }

    // The interactive row nearest index, searched outward and preferring the earlier row at a tie,
    // because the reader came from above. Returns index where the page has no interactive row.
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

    private string? BuildRoot(List<OverlayRow> rows)
    {
        if (HasScenes)
        {
            rows.Add(new OverlayRow("Scene", () => Open(PageKind.Scene), OverlayActions.ScenePage, OpensMenu: true));
        }

        rows.Add(new OverlayRow("Step", StepGame, OverlayActions.Step, Repeats: true));
        rows.Add(new OverlayRow("Debug Draw", OpenDebugDraw, OverlayActions.DebugDraw, OpensMenu: true));
        rows.Add(new OverlayRow("Time Scale", () => Open(PageKind.TimeScale), OverlayActions.TimeScale, OpensMenu: true));

        if (HasScenes)
        {
            rows.Add(new OverlayRow("Restart", Restart, OverlayActions.Restart));

            if (Registrations.Count > 0)
            {
                rows.Add(new OverlayRow("Load Scene", () => Open(PageKind.LoadScene), OverlayActions.LoadScene, OpensMenu: true));
            }
        }

        rows.Add(new OverlayRow("Frame Pane", ToggleFramePane, OverlayActions.FramePane));
        rows.Add(new OverlayRow("Hide", Hide, OverlayActions.Hide));

        if (HasScenes)
        {
            rows.Add(new OverlayRow("Exit", Exit, OverlayActions.Exit));
        }

        return null;
    }

    // A row per channel that has emitted, in name order, its label carrying the channel's state.
    private string BuildDebugDraw(List<OverlayRow> rows)
    {
        foreach (string channel in Channels)
        {
            string label = (IsChannelEnabled(channel) ? "[x] " : "[ ] ") + channel;
            rows.Add(new OverlayRow(label, () => ToggleChannel(channel)));
        }

        if (rows.Count == 0)
        {
            rows.Add(new OverlayRow("<No channel has emitted yet>", null));
        }

        return "Debug Draw";
    }

    // A row per pace on the ladder, marking the pace in force. No row is marked when a game set a pace
    // off the ladder.
    private string BuildTimeScale(List<OverlayRow> rows)
    {
        foreach ((double scale, string label) in TimeScales)
        {
            rows.Add(new OverlayRow((IsTimeScale(scale) ? "(x) " : "( ) ") + label, () => SetTimeScale(scale)));
        }

        return "Time Scale";
    }

    // Every registered class by name. A document-backed class is requested by its document's name, the
    // form the registry composes it from.
    private string BuildLoadScene(List<OverlayRow> rows)
    {
        List<SceneRegistration> registrations = [.. Registrations];
        registrations.Sort(static (a, b) => string.CompareOrdinal(a.SceneType.Name, b.SceneType.Name));

        foreach (SceneRegistration registration in registrations)
        {
            SceneTransition target = registration.DocumentName is { } name
                ? SceneTransition.ToName(name, null)
                : SceneTransition.ToScene(registration.SceneType, null);
            rows.Add(new OverlayRow(registration.SceneType.Name, () => Load(in target)));
        }

        return "Load Scene";
    }

    // Opens the Debug Draw page. The scene is asked to emit first, which lets a run held before its
    // first step still list its channels.
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
        SetStale();
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
        SetStale();
    }

    // A request the run declines, because a transition is already pending, is shown on the status line
    // and not stepped, or the pending transition would be stepped in its place. A request the run or
    // the host refuses is shown and logged in full. Either way the run stays held on its current scene.
    // Refused or not, the request is the act: either path can leave something for the page to show.
    private void Request(string action, in SceneTransition transition)
    {
        SetStale();
        try
        {
            if (!GameRun.TryRequest(in transition))
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

    // The stepped tick that follows a host act and consumes whatever transition the act requested. An
    // incoming scene that fails to come up is shown on the status line and logged in full, and the run
    // stays on its current scene. The tick's own failure propagates as it does from the Step row, since
    // the simulation does not continue after it.
    private void StepGame(string action, Action? before)
    {
        try
        {
            StepGame(before);
        }
        catch (Exception failure) when (GameHost.TransitionFailed)
        {
            Report(action, failure);
        }
    }

    // `before` is a host act that belongs to the tick. It runs inside the step, after the step begins
    // and ahead of the scene's own work, so what it changes and the sounds it asks for are this
    // step's.
    private void StepGame(Action? before)
    {
        _exited = _scheduler.StepOnce(in _stripped, _simulation, before);
        _steppedTicks += _scheduler.StepsThisFrame;
        SettleDraws();
        RefreshReadout();
        SetStale();
    }

    private void Report(string action, Exception failure)
    {
        Log.Error($"{action} from the debug menu failed: {failure}");
        Scene.SetStatus($"{action} failed: {failure.GetType().Name}: {failure.Message}");
    }

    // Closes the frame Observe opened and hands the pane its sample. The first frame has no
    // predecessor to measure an interval against and is not sampled. A Step with no Observe before it
    // is not a frame.
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
            _lastFrame.Milliseconds,
            steps));
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    // The overlay's canvas is the back buffer at its own integer scale, so one canvas pixel is that
    // many window pixels.
    private void Refit((int Width, int Height) backBuffer)
    {
        (int width, int height) = backBuffer;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int scale = ScaleFor(height);
        Vector2 canvas = new(width / (float)scale, height / (float)scale);
        if (_overlayRun.Canvas != canvas)
        {
            _overlayRun.Canvas = canvas;
        }
    }

    // The held scene's debug pass into the buffer, run when the buffer is attached, there is a run of
    // scenes to ask, and the step the settled frame shows has not already drawn into it. The buffer is
    // settled first at that step's tick, so the pass stamps as the step's would have and expires with
    // the next step.
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

    // Once per frame and again after a stepped tick, so the frame that stepped shows what that step
    // left. A frame that ran several steps settles once at its last tick, so draws emitted by its
    // later steps are stamped with its first and leave up to that many ticks early.
    private void SettleDraws() => _buffer.Settle(_scheduler.Tick);

    // The buffer is attached only while what it holds can be seen, and an ordinary frame runs no
    // debug-draw walk. As a result the Debug Draw page lists the channels that emitted while the
    // overlay was open, not every channel the run has drawn on since boot.
    private void AttachBuffer() =>
        DebugDraw.UseBuffer(_state != OverlayState.Closed || _enabledChannels.Count > 0 ? _buffer : null);

    // One open page: which page it is, the entity a panel is for and the name it had when opened, and
    // the row that opened it, which the focus returns to on pop.
    private readonly record struct Page(PageKind Kind, Entity? Subject, int ReturnFocus, string? Name);
}
