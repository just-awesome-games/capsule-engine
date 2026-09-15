using System.Diagnostics;
using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// Hosts the overlay scene beside the game — the toggle, the input quarantine, the hold, and
// the stepping and drawing of that scene — and owns every data source and action its menu offers;
// nothing of how the menu looks, which is OverlayScene's.
internal sealed class OverlayHost : IDisposable
{
    // The host paces the Time Scale submenu offers, slowest first, each with the label its row
    // carries. 4x is the top: the default frame budget is eight steps a frame, and past it a frame
    // drops the backlog it cannot run rather than running faster.
    internal static readonly (double Scale, string Label)[] TimeScales =
        [(0.25, "0.25x"), (0.5, "0.5x"), (1, "1x"), (2, "2x"), (4, "4x")];

    private readonly FixedStepScheduler _scheduler;
    private readonly ISimulation _simulation;
    private readonly SceneHost? _scenes;
    private readonly SimulationHost _host;

    // Where the game's DebugDraw calls land while this overlay is attached, the channels switched
    // on — every channel starts off — and the renderer that reads the two onto the scene's world.
    private readonly DebugDrawBuffer _buffer = new();
    private readonly HashSet<string> _enabledChannels = new(StringComparer.Ordinal);
    private readonly DebugDrawRenderer _draws;

    // One submenu for the run, filled on first open and refilled only when a channel has arrived
    // or a toggle flipped, so its focus and identity survive.
    private Menu? _debugDrawMenu;

    // One submenu for the run, filled on first open and refilled when a pace is chosen, so its
    // focus and identity survive.
    private Menu? _timeScaleMenu;

    // The scene page and entity panels over the held scene; none without a run of scenes.
    private readonly PanelMenus? _panels;

    // The toggle first, then every button the menu binds; each is stripped from the game's
    // snapshot while the overlay is open and, once it is not, until the button is released.
    private readonly InputButton[] _quarantine;
    private readonly bool[] _withheld;

    private OverlayState _state;
    private bool _toggleDown;
    private bool _hideDown;
    private bool _framePaneOn;

    // Ticks the menu stepped by hand since the last sample. They run inside the overlay's own step,
    // after the frame is sampled, and the next advance clears the scheduler's count, so they are
    // tallied here and counted on the frame that follows.
    private int _steppedTicks;

    // The frame's clock: Observe stamps its start, Step reads the update bracket and the interval
    // from the previous frame's start. Negative while no frame is in progress or none has been.
    private readonly Func<long> _timestamp;
    private long _observed = -1;
    private long _previousObserved = -1;

    // Set by the hide press that showed the overlay and cleared on its release: that press is
    // withheld from the menu, or the menu would read it as a fresh press and hide again.
    private bool _hidePressConsumed;

    // The frame's device state as sampled, which the menu reads, and with the menu's buttons
    // stripped, which a stepped tick reads.
    private DeviceSnapshot _sampled;
    private DeviceSnapshot _stripped;

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
        _panels = scenes is { } held ? new PanelMenus(Scene, held, activate => Tick("Command", activate)) : null;

        // Withdrawn before the host builds the first frame, which is what is drawn until a Step
        // rewrites it.
        Scene.Push(Menu.Default(this));
        Scene.ShowMenu(false);

        _host = new SimulationHost(
            Scene,
            new InputState(OverlayActions.Bindings),
            run: new Run
            {
                Canvas = Run.StandardCanvas,
                Sampling = TextureSampling.Point,
                EmitsDebugDraw = false,
            });

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

    internal OverlayScene Scene { get; }

    internal SimulationHost Host => _host;

    internal bool HasScenes => _scenes is not null;

    internal IReadOnlyCollection<SceneRegistration> Registrations { get; }

    internal bool IsOpen => _state == OverlayState.Open;

    internal bool IsHidden => _state == OverlayState.Hidden;

    internal bool IsFramePaneOn => _framePaneOn;

    // Raised on each edge of the hold — true as the overlay takes it, false as it lets go — for
    // whatever host presentation follows the simulation's standstill.
    internal Action<bool>? HoldChanged { get; set; }

    // The last game frame's cost as the renderer measured it, read from the renderer by Step; set
    // directly where there is no renderer to read.
    internal RenderStats LastFrame { get; set; }

    // Flips the pane for the rest of the play session; it draws on the overlay's next frame, menu
    // open, closed or hidden.
    internal void ToggleFramePane()
    {
        _framePaneOn = !_framePaneOn;
        if (_framePaneOn)
        {
            Scene.Pane.Reset();
        }

        Scene.ShowFramePane(_framePaneOn);
    }

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

    // Flips a channel for the rest of the play session. Draws follow on the overlay's next frame,
    // whether or not the game steps; the submenu's row follows at once, keeping its focus.
    internal void ToggleChannel(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (!_enabledChannels.Remove(channel))
        {
            _enabledChannels.Add(channel);
        }

        AttachBuffer();
        Refill(_debugDrawMenu, DebugDrawRows());
    }

    // Pushes the submenu, filling it the first time. With no channel emitted yet there is nothing
    // to list, so the status line says so instead.
    internal void OpenDebugDraw()
    {
        if (_buffer.Channels.Count == 0)
        {
            Scene.SetStatus("No debug draw channel has emitted yet");
            return;
        }

        _debugDrawMenu ??= new Menu("Debug Draw", DebugDrawRows());
        Scene.Push(_debugDrawMenu);
    }

    // Pushes the held scene's page.
    internal void OpenScenePage() =>
        (_panels ?? throw new InvalidOperationException("The debug menu's Scene needs a run of scenes.")).Open();

    // A row per channel that has emitted, in name order, its label carrying the channel's state.
    private List<MenuItem> DebugDrawRows()
    {
        string[] channels = Channels;
        List<MenuItem> rows = new(channels.Length);
        foreach (string channel in channels)
        {
            rows.Add(new MenuItem(
                (IsChannelEnabled(channel) ? "[x] " : "[ ] ") + channel,
                () => ToggleChannel(channel)));
        }

        return rows;
    }

    // The host pace, which is the game's on a run of scenes — a value a game set marks its ladder
    // row, and a ladder pick is visible to the game — and the scheduler's own where there is no run
    // to hold it. Written, the applied value follows at once rather than waiting for the host's
    // next copy of the run's.
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

    // Whether scale is the pace in force. A pace a game set off the ladder matches no row.
    internal bool IsTimeScale(double scale) => Pace == scale;

    // Sets the pace for the rest of the run — the overlay never resets it, and nothing of the
    // simulation changes — so the submenu's marks follow at once, keeping their focus, and no tick
    // is stepped.
    internal void SetTimeScale(double scale)
    {
        Pace = scale;
        Refill(_timeScaleMenu, TimeScaleRows());
    }

    // Pushes the submenu, built the first time and filled on every open: the game may have moved
    // the run's pace since it was last filled. The ladder is fixed, so there is never nothing to
    // list.
    internal void OpenTimeScale()
    {
        if (_timeScaleMenu is null)
        {
            _timeScaleMenu = new Menu("Time Scale", TimeScaleRows());
        }
        else
        {
            Refill(_timeScaleMenu, TimeScaleRows());
        }

        Scene.Push(_timeScaleMenu);
    }

    // A row per pace on the ladder, marked where it is the one in force; exactly one is, unless a
    // game set a pace off the ladder.
    private List<MenuItem> TimeScaleRows()
    {
        List<MenuItem> rows = new(TimeScales.Length);
        foreach ((double scale, string label) in TimeScales)
        {
            rows.Add(new MenuItem((IsTimeScale(scale) ? "(x) " : "( ) ") + label, () => SetTimeScale(scale)));
        }

        return rows;
    }

    // Rewrites a marked submenu's rows, and the frame beneath it when it is the one on screen, so
    // a mark follows the act at once while the menu keeps its focus and identity.
    private void Refill(Menu? menu, IReadOnlyList<MenuItem> rows)
    {
        if (menu is null)
        {
            return;
        }

        menu.Fill(rows);
        if (ReferenceEquals(Scene.Current, menu))
        {
            Scene.Replace(menu);
        }
    }

    internal string Readout
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            RefreshReadout();

            return Scene.Readout;
        }
    }

    // The host calls this before the game's scheduler with the frame's device state, its pointer
    // already mapped onto the game's canvas, and hands the game what it returns.
    internal DeviceSnapshot Observe(DeviceSnapshot snapshot, FrameRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        (int _, int height) = renderer.BackBufferSize;

        return Observe(snapshot, renderer.ScreenLayer, ScaleFor(height));
    }

    // gameLayer is where the game's canvas landed in the window and overlayScale the overlay's own
    // integer scale, which together carry the pointer from the game's canvas to the overlay's; a
    // placement of no scale leaves it where it is. The game's snapshot keeps its own pointer.
    internal DeviceSnapshot Observe(DeviceSnapshot snapshot, in ScreenPlacement gameLayer = default, int overlayScale = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _observed = _timestamp();

        // Toggle: the leading edge, read on the raw state, takes closed to open, open to closed and
        // hidden back to open. The toggle is withheld from the menu, so a toggle that is also a menu
        // key does not fire that key's action on the frame it opens.
        InputButton toggle = _quarantine[0];
        bool wasOpen = _state == OverlayState.Open;
        bool toggleDown = toggle.IsDown(snapshot);
        if (toggleDown && !_toggleDown)
        {
            // Closed to open: the game ran, or replaced its scene, since the menus were last
            // built. Hidden to open needs nothing, since the game was held throughout.
            if (_state == OverlayState.Closed)
            {
                _panels?.Invalidate();
            }

            _state = wasOpen ? OverlayState.Closed : OverlayState.Open;
            AttachBuffer();
            bool held = _state != OverlayState.Closed;
            if (held != _scheduler.Held)
            {
                _scheduler.Held = held;
                HoldChanged?.Invoke(held);
            }
        }

        _toggleDown = toggleDown;
        DeviceSnapshot sampled = toggle.IsNone ? snapshot : snapshot.Without(toggle);

        // Hide: the scene is not stepped while hidden, so the edge that shows it again is read here,
        // and that press is withheld from the menu until released.
        bool hideDown = OverlayActions.Bindings.IsAnyDown(OverlayActions.Hide, snapshot);
        if (hideDown && !_hideDown && _state == OverlayState.Hidden)
        {
            _state = OverlayState.Open;
            _hidePressConsumed = true;
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

        // Quarantine: every listed button is withheld from the game while the overlay is open and,
        // once it is not, through each one's release, so a press that served the menu cannot land
        // on the resumed step.
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

        // The wheel serves the menu while the overlay is open, so a stepped tick sees none of it.
        if (open)
        {
            snapshot = snapshot.WithScroll(Vector2.Zero);
        }

        _stripped = snapshot;

        return snapshot;
    }

    // The host calls this after the game's scheduler. The game is held while the overlay is open,
    // so this advances the overlay once on the frame's sampled snapshot; otherwise the overlay's
    // frame is rewritten without a step, so the draws and the pane follow the game's ticks and the
    // toggles.
    internal void Step(FrameRenderer? renderer = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The update bracket ends here, ahead of the overlay's own work.
        long stepped = _timestamp();

        SettleDraws();

        if (_exited)
        {
            return;
        }

        if (renderer is not null)
        {
            (int Width, int Height) backBuffer = renderer.BackBufferSize;
            Refit(backBuffer);

            // Font pixels into world units at the last frame's placement, so a label stays the
            // menu's glyph size at any zoom; unity until a frame has drawn a world.
            float pixelsPerUnit = renderer.WorldPixelsPerUnit;
            _draws.TextScale = pixelsPerUnit > 0f ? ScaleFor(backBuffer.Height) / pixelsPerUnit : 1f;
            LastFrame = renderer.LastFrame;
        }

        SampleFrame(stepped);

        // The frame's own alpha, which the game frame is about to be drawn at; one while held.
        _draws.Alpha = _scheduler.InterpolationAlpha;

        bool open = _state == OverlayState.Open;
        bool menuChanged = Scene.ShowMenu(open);

        if (open)
        {
            // A channel that emitted since the submenu was last filled gets its row before the
            // menu reads this frame's input.
            if (_debugDrawMenu is { } menu && menu.Items.Count != _buffer.Channels.Count)
            {
                Refill(menu, DebugDrawRows());
            }

            RefreshReadout();

            // A page that became current since the last stepped tick is rebuilt before the menu
            // reads this frame's input, and again after it: a Back inside the step can expose a
            // page beneath that a command, step or load left stale, and the frame that exposed
            // it shows it rebuilt or popped rather than as it was.
            _panels?.Refresh();
            _host.Step(in _sampled);
            bool exposed = _panels?.Refresh() ?? false;

            // Hidden from inside the step: the menu leaves before this frame draws.
            if ((_state != OverlayState.Open && Scene.ShowMenu(false)) || exposed)
            {
                _host.Simulation.RewriteView();
            }
        }
        else if (menuChanged || _enabledChannels.Count > 0 || _framePaneOn)
        {
            _host.Simulation.RewriteView();
        }
    }

    // Closes the frame Observe opened and hands the pane its sample. The first frame has no
    // predecessor to measure an interval against and is not sampled; a Step with no Observe before
    // it is not a frame.
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
            LastFrame.Milliseconds,
            steps));
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    internal void Draw(FrameRenderer renderer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderer);

        // Nothing of the overlay is on screen — no menu, no pane, no channel — so the frame it
        // would submit holds nothing. Step has already refitted this back buffer.
        if (_exited || (_state == OverlayState.Closed && !_framePaneOn && _enabledChannels.Count == 0))
        {
            return;
        }

        (int _, int height) = renderer.BackBufferSize;
        renderer.DrawOverlay(_host.Simulation.View, ScaleFor(height));
    }

    internal void Refit((int Width, int Height) backBuffer)
    {
        (int width, int height) = backBuffer;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int scale = ScaleFor(height);
        Vector2 canvas = new(width / (float)scale, height / (float)scale);
        if (_host.Run.Canvas != canvas)
        {
            _host.Run.Canvas = canvas;
        }
    }

    internal static int ScaleFor(int height) => height < 1080 ? 1 : height < 2160 ? 2 : 3;

    // A stepped tick is an ordinary tick: a game that throws inside one crashes as it always did.
    // The readout and the pages are refreshed inside the menu's own step, so the frame that
    // stepped shows the result; an exit tore the scene down, so nothing is read from it.
    internal void StepGame() => StepGame(null);

    // `before` is a host act that is part of the tick: it runs inside the step, once the step has
    // begun and ahead of the scene's own work, so what it changes and the sounds it asks for are
    // this step's.
    private void StepGame(Action? before)
    {
        _exited = _scheduler.StepOnce(in _stripped, _simulation, before);
        _steppedTicks += _scheduler.StepsThisFrame;
        SettleDraws();
        RefreshReadout();
        if (!_exited && _panels is { } panels)
        {
            panels.Invalidate();
            panels.Refresh();
        }
    }

    internal void Hide() => _state = OverlayState.Hidden;

    internal void Restart() => Request("Restart", SceneTransition.Restart(null, false));

    internal void Load(in SceneTransition transition) => Request("Load", in transition);

    internal void Exit()
    {
        GameRun.RequestExit();
        StepGame();
    }

    void IDisposable.Dispose() => Dispose();

    internal void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DebugDraw.UseBuffer(null);
        _host.Dispose();
    }

    private SceneHost GameHost =>
        _scenes ?? throw new InvalidOperationException("The debug menu's scene actions need a run of scenes.");

    private Run GameRun => GameHost.Run;

    // A request the run declines — a transition already pending, one a scene asked for in its own
    // start — is shown on the status line and not stepped, or the pending one would be stepped in
    // its place. A request the run refuses — exit already asked for — or a transition the host
    // refuses — a scene whose start requires a payload — is shown on the status line and logged in
    // full. Either way the run stays held on the scene it was on.
    private void Request(string action, in SceneTransition transition)
    {
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

        Tick(action, null);
    }

    // The one stepped tick a host act is followed by — a menu load or restart, a command or
    // toggle from a panel — which consumes whatever transition the act requested. An incoming
    // scene that fails to come up is shown on the status line and logged in full, the run held
    // on the scene it was on, and the pages are rebuilt over what the act changed all the same;
    // the tick's own failure — the scene's step, a callback, a contact — propagates as it does
    // from the Step row, since the simulation is not continued after it.
    private void Tick(string action, Action? before)
    {
        try
        {
            StepGame(before);
        }
        catch (Exception failure) when (GameHost.TransitionFailed)
        {
            Report(action, failure);
            if (_panels is { } panels)
            {
                panels.Invalidate();
                panels.Refresh();
            }
        }
    }

    private void Report(string action, Exception failure)
    {
        Log.Error($"{action} from the debug menu failed: {failure}");
        Scene.SetStatus($"{action} failed: {failure.GetType().Name}: {failure.Message}");
    }

    private void RefreshReadout() =>
        Scene.SetReadout(_scenes is { } scenes ? scenes.Scene.GetType().Name : string.Empty, _scheduler.Tick);

    // Once per frame and again after a stepped tick, so the frame that stepped shows what that
    // step left. A frame that ran several steps settles once, at its last tick: draws emitted by
    // its later steps are stamped with its first, and so leave up to that many ticks early.
    private void SettleDraws() => _buffer.Settle(_scheduler.Tick);

    // The buffer is attached only while what it holds can be seen — the overlay open or hidden, or
    // a channel switched on — so an ordinary frame runs no debug-draw walk at all. The consequence
    // is that the Debug Draw menu lists the channels that emitted while the overlay was open, not
    // every channel the run has drawn on since boot.
    private void AttachBuffer() =>
        DebugDraw.UseBuffer(_state != OverlayState.Closed || _enabledChannels.Count > 0 ? _buffer : null);
}
