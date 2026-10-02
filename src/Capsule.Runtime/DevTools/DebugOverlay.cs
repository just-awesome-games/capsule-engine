using System.Diagnostics;
using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// The overlay holds the run while open. The page rebuilds only after the host's own acts.
internal sealed class DebugOverlay : IDisposable
{
    // Overlay frames before a held key repeats, and between repeats.
    internal const int RepeatDelayFrames = 60;
    internal const int RepeatIntervalFrames = 10;

    // Slowest first. 4x is the top because the default frame budget is eight steps.
    internal static readonly (double Scale, string Label)[] TimeScales =
        [(0.25, "0.25x"), (0.5, "0.5x"), (1, "1x"), (2, "2x"), (4, "4x")];

    private readonly FixedStepScheduler _scheduler;
    private readonly SceneHost _scenes;
    private readonly Func<long> _timestamp;

    // The overlay's own scene is never stepped. Its rows are written straight onto it.
    private readonly SceneSimulation _sceneSimulation;
    private readonly Run _sceneRun;

    // Advanced every frame. Rows act on it only while open.
    private readonly InputState _input = new(OverlayActions.Bindings);
    private readonly DebugDrawRenderer _draws;
    private readonly PanelRows _panelRows;
    private readonly FreeCamera _freeCamera;

    private readonly List<Page> _pages = [new Page(PageKind.Root, null, 0, null)];

    private readonly List<OverlayRow> _rows = [];
    private readonly RowCursor _cursor;

    // The root's rows never change. They carry the hotkeys at any depth.
    private readonly List<OverlayRow> _rootRows = [];

    // Built once. Registrations are fixed at construction.
    private readonly List<OverlayRow> _loadRows = [];

    // The toggle never reaches the game. A press that closes the overlay is held until released.
    private readonly InputButton _toggle;

    // Stripped from the game while open or hidden, and after closing until released.
    private readonly InputButton[] _overlayButtons;
    private readonly bool[] _withheld;

    private OverlayState _state;
    private bool _toggleDown;
    private bool _framePaneOn;
    private string? _title;
    private HoldRepeat _repeat;

    // Set by every host act that could change the page. A build clears it.
    private bool _pageStale;

    // False after a build or a status change, until the scene has laid the page out again.
    private bool _pageShown;

    // Ticks stepped by hand run after the frame is sampled, so they count toward the next sample.
    private int _ticksSteppedByHand;

    // Intercept stamps the frame's start. Negative while no frame is in progress.
    private long _frameStart = -1;
    private long _previousFrameStart = -1;

    // Set on the frame a Hide press shows the menu again. The Hide row skips that press.
    private bool _shownByHide;

    // Whether the pointer stood on the menu as the frame's input was read. The wheel scrolls the rows
    // only there.
    private bool _overMenu;

    // The game's snapshot this frame, for a tick stepped by hand.
    private DeviceSnapshot _gameInput;

    // The last world point the pointer stood over, outside the menu, while held. It belongs
    // to the scene it was read in, and closing or a change of scene drops it.
    private Vector2? _pointerWorld;
    private Scene? _pointerScene;

    private double _lastFrameMs;
    private bool _exited;
    private bool _disposed;

    internal OverlayScene Scene { get; }

    // Raised on each edge of the hold: true as the overlay takes it, false as it lets go.
    internal Action<bool>? HoldChanged { get; set; }

    internal DebugOverlay(
        InputButton button,
        FixedStepScheduler scheduler,
        SceneHost scenes,
        SceneRegistry? registry = null,
        Func<long>? timestamp = null)
    {
        _scheduler = scheduler;
        _scenes = scenes;
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _toggle = button;
        _cursor = new RowCursor(_rows);

        Scene = new OverlayScene(OverlayActions.KeyName(button));
        _draws = new DebugDrawRenderer(scheduler, scenes);
        Scene.Add(new DebugDrawEntity(_draws));
        _panelRows = new PanelRows(scenes, activate => StepGameReporting("Command", activate), Open, () => Open(PageKind.Camera));
        _freeCamera = new FreeCamera(scenes, scheduler);
        BuildRoot(registry?.Registrations ?? []);

        _sceneRun = new Run
        {
            Canvas = Run.StandardCanvas,
            Sampling = TextureSampling.Point,
            EmitsDebugDraw = false,
        };

        // Withdrawn before the simulation writes its first frame.
        Scene.ShowMenu(false);
        _sceneSimulation = new SceneSimulation(Scene, run: _sceneRun);

        List<InputButton> overlayButtons = [];
        foreach (InputAction action in OverlayActions.Actions)
        {
            overlayButtons.AddRange(OverlayActions.Bindings.ButtonsFor(action));
        }

        _overlayButtons = [.. overlayButtons];
        _withheld = new bool[_overlayButtons.Length];

        _draws.Attach();
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
        Camera,
        DebugDraw,
        TimeScale,
        LoadScene,
    }

    internal FrameView View => _sceneSimulation.View;

    internal bool IsOpen => _state == OverlayState.Open;

    internal bool IsHidden => _state == OverlayState.Hidden;

    internal bool IsFramePaneOn => _framePaneOn;

    internal IReadOnlyList<OverlayRow> Rows => _rows;

    internal string? Title => _title;

    internal int Focus => _cursor.Focus;

    internal int Depth => _pages.Count;

    internal DebugDrawRenderer Draws => _draws;

    internal FreeCamera FreeCamera => _freeCamera;

    internal static int ScaleFor(int height) => height < 1080 ? 1 : height < 2160 ? 2 : 3;

    // Runs before the game's scheduler and returns the snapshot the game sees.
    // gameLayer and overlayScale map the pointer onto the overlay's canvas. world is where the last
    // frame's world landed, or null before one has.
    internal DeviceSnapshot Intercept(
        DeviceSnapshot snapshot,
        in ScreenPlacement gameLayer = default,
        int overlayScale = 1,
        WorldPlacement? world = null)
    {
        _frameStart = _timestamp();

        // The toggle's leading edge takes closed to open, open to closed and hidden to open. It is read raw
        // because the game configures it and it acts before the overlay holds anything.
        bool wasOpen = _state == OverlayState.Open;
        bool wasHeld = _state != OverlayState.Closed;
        bool toggleDown = _toggle.IsDown(snapshot);
        if (toggleDown && !_toggleDown)
        {
            _state = wasOpen ? OverlayState.Closed : OverlayState.Open;
            bool held = _state != OverlayState.Closed;
            if (held != _scheduler.Held)
            {
                _scheduler.Held = held;
                HoldChanged?.Invoke(held);
            }

            _draws.Attach();
        }

        _toggleDown = toggleDown;
        DeviceSnapshot game = _toggle.IsNone ? snapshot : snapshot.Without(_toggle);

        Vector2 window = game.Pointer;
        DeviceSnapshot overlayInput = game;
        if (gameLayer.Scale > 0f && overlayScale > 0)
        {
            window = (game.Pointer * gameLayer.Scale) + gameLayer.Origin;
            overlayInput = game.WithPointer(window / overlayScale);
        }

        // Advanced while closed too, so a press made before the overlay opens is no edge once it has.
        _input.Advance(in overlayInput);

        // The menu is withdrawn while hidden, so the keys that act then are read here.
        _shownByHide = _state == OverlayState.Hidden && _input.WasPressed(OverlayActions.Hide);
        if (_shownByHide)
        {
            _state = OverlayState.Open;
        }
        else if (_state == OverlayState.Hidden && _input.WasPressed(OverlayActions.GameCamera))
        {
            _freeCamera.Attach();
        }

        // The wheel scrolls the rows over the menu and moves the free camera over the world.
        _overMenu = Scene.CoversMenu(overlayInput.Pointer);
        bool holding = _state != OverlayState.Closed;
        if (!holding)
        {
            _pointerWorld = null;
        }

        bool overWorld = false;
        if (holding && world is { } placement && placement.Contains(window) && !_overMenu)
        {
            overWorld = true;
            _pointerWorld = placement.ToWorld(window);
        }

        _freeCamera.Read(_input, window, overWorld, world, holding);

        // A press that served the overlay cannot land on the resumed step.
        holding |= wasHeld;
        for (int index = 0; index < _overlayButtons.Length; index++)
        {
            if (holding || _withheld[index])
            {
                InputButton button = _overlayButtons[index];
                _withheld[index] = button.IsDown(snapshot);
                game = game.Without(button);
            }
        }

        if (holding)
        {
            game = game.WithScroll(Vector2.Zero);
        }

        _gameInput = game;

        return game;
    }

    // Runs after the game's scheduler. A host with no renderer passes the frame's cost.
    internal void Update(FrameRenderer? renderer = null, double lastFrameMs = 0)
    {
        long gameEnd = _timestamp();

        _draws.Settle();

        if (_exited)
        {
            return;
        }

        _lastFrameMs = lastFrameMs;
        if (renderer is not null)
        {
            (int width, int height) = renderer.BackBufferSize;
            int scale = ScaleFor(height);
            if (width > 0 && height > 0)
            {
                _sceneRun.Canvas = new Vector2(width / (float)scale, height / (float)scale);
            }

            // Font pixels to world units, which keeps labels glyph-sized at any zoom.
            float pixelsPerUnit = renderer.WorldPixelsPerUnit;
            _draws.TextScale = pixelsPerUnit > 0f ? scale / pixelsPerUnit : 1f;
            _lastFrameMs = renderer.LastFrameMs;
            renderer.WorldZoomOut = _freeCamera.ZoomOut;
        }

        SampleFrame(gameEnd);
        _draws.Alpha = _scheduler.InterpolationAlpha;

        bool open = _state == OverlayState.Open;
        bool menuChanged = Scene.ShowMenu(open);
        if (open)
        {
            // The run stepped freely while the overlay was shut.
            if (menuChanged)
            {
                _pageStale = true;
                _cursor.StopScroll();
            }

            ReadRows();

            // An act may have hidden the overlay from inside its own frame.
            if (_state == OverlayState.Open)
            {
                ForgetPointerOnSceneChange();
                Scene.SetPointer(_pointerWorld);
                BuildRows();
                if (!_pageShown)
                {
                    Scene.ShowPage(_title, _rows);
                    _pageShown = true;
                }

                Scene.Place(_cursor.Focus, _cursor.Top);
            }
            else
            {
                Scene.ShowMenu(false);
            }

            _sceneSimulation.RewriteView();
        }
        else if (menuChanged || _draws.AnyEnabled || _framePaneOn)
        {
            _sceneSimulation.RewriteView();
        }
    }

    internal void Draw(FrameRenderer renderer)
    {
        if (_exited || (_state == OverlayState.Closed && !_framePaneOn && !_draws.AnyEnabled))
        {
            return;
        }

        renderer.DrawOverlay(_sceneSimulation.View, ScaleFor(renderer.BackBufferSize.Height));
    }

    internal void ToggleFramePane()
    {
        _framePaneOn = !_framePaneOn;
        if (_framePaneOn)
        {
            Scene.Pane.Reset();
        }

        Scene.ShowFramePane(_framePaneOn);
    }

    internal void ToggleChannel(string channel)
    {
        _draws.Toggle(channel);
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
        _sceneSimulation.Dispose();
    }

    // A registration is named by its class, or by its document's key when it has none.
    private void BuildRoot(IReadOnlyList<SceneRegistration> registrations)
    {
        foreach (SceneRegistration registration in registrations)
        {
            SceneTransition target = registration.DocumentName is { } name
                ? SceneTransition.ToName(name, null)
                : SceneTransition.ToScene(registration.SceneType!, null);
            _loadRows.Add(new OverlayRow(registration.Name, () => Load(in target)));
        }

        _loadRows.Sort(static (a, b) => OverlayRow.CompareLabels(a.Label, b.Label));

        Add("Scene", () => Open(PageKind.Scene), OverlayActions.ScenePage, opensPage: true);
        Add("Step", () => StepGame(null), OverlayActions.Step, repeats: true);
        Add("Debug Draw", OpenDebugDraw, OverlayActions.DebugDraw, opensPage: true);
        Add("Time Scale", () => Open(PageKind.TimeScale), OverlayActions.TimeScale, opensPage: true);
        Add("Restart", () => Request("Restart", SceneTransition.Restart(null, false)), OverlayActions.Restart);
        if (_loadRows.Count > 0)
        {
            Add("Load Scene", () => Open(PageKind.LoadScene), OverlayActions.LoadScene, opensPage: true);
        }

        Add("Game Camera", _freeCamera.Attach, OverlayActions.GameCamera);
        Add("Frame Pane", ToggleFramePane, OverlayActions.FramePane);
        Add("Hide", () => _state = OverlayState.Hidden, OverlayActions.Hide);
        Add("Exit", Exit, OverlayActions.Exit);

        void Add(string label, Action activate, InputAction hotkey, bool repeats = false, bool opensPage = false) =>
            _rootRows.Add(new OverlayRow(label, activate, OverlayActions.KeyName(hotkey), hotkey, repeats, opensPage));
    }

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

        bool repeat = _repeat.Next(held, pressed, RepeatDelayFrames, RepeatIntervalFrames);

        if (_input.WasPressed(OverlayActions.Back))
        {
            Pop();
            BuildRows();
        }

        if (Pressed(OverlayActions.MenuDown, repeat))
        {
            _cursor.Move(1);
        }

        if (Pressed(OverlayActions.MenuUp, repeat))
        {
            _cursor.Move(-1);
        }

        _cursor.Scroll(_overMenu ? _input.Axis(OverlayActions.Scroll) : 0f);

        if (_cursor.Point(Scene.RowAt(_input.Pointer), _input.PointerMoved, _input.WasPressed(OverlayActions.Click)))
        {
            Activate(_rows[_cursor.Focus]);
        }

        if (_input.WasPressed(OverlayActions.Confirm) && _cursor.Focus < _rows.Count)
        {
            Activate(_rows[_cursor.Focus]);
        }

        bool atRoot = _pages.Count == 1;
        foreach (OverlayRow row in _rootRows)
        {
            if (row.Hotkey is { } hotkey && (atRoot || !row.OpensPage) && !(_shownByHide && hotkey == OverlayActions.Hide)
                && Pressed(hotkey, row.Repeats && repeat))
            {
                Activate(row);
            }
        }
    }

    private bool Pressed(InputAction action, bool repeat) =>
        _input.WasPressed(action) || (repeat && _input.IsHeld(action));

    private void Activate(in OverlayRow row)
    {
        if (row.Activate is { } activate)
        {
            SetStatus(string.Empty);
            activate();
        }
    }

    private void BuildRows()
    {
        if (!_pageStale)
        {
            return;
        }

        _pageShown = false;
        while (_pages[^1] is { Kind: PageKind.Entity } departed && !_panelRows.Holds(departed.Subject!))
        {
            // The name it had when opened. An entity out of the scene has no place among its siblings.
            SetStatus($"{departed.Name} left the scene");
            Pop();
        }

        // Cleared after the pops, which mark the page stale, or the surviving page builds twice.
        _pageStale = false;
        _rows.Clear();
        Page page = _pages[^1];
        _title = page.Kind switch
        {
            PageKind.Root => Copy(_rootRows, null),
            PageKind.Scene => _panelRows.ScenePage(_rows),
            PageKind.Entity => _panelRows.EntityPanel(page.Subject!, _rows),
            PageKind.Camera => _panelRows.CameraPanel(_rows),
            PageKind.DebugDraw => BuildDebugDraw(),
            PageKind.TimeScale => BuildTimeScale(),
            _ => Copy(_loadRows, "Load Scene"),
        };

        _cursor.Fit();
        Scene.SetReadout(_scenes.Scene.GetType().Name, _scheduler.Tick);
    }

    // A row's act may have moved the run to another scene since the point was read.
    private void ForgetPointerOnSceneChange()
    {
        if (!ReferenceEquals(_pointerScene, _scenes.Scene))
        {
            _pointerScene = _scenes.Scene;
            _pointerWorld = null;
        }
    }

    private string? Copy(List<OverlayRow> rows, string? title)
    {
        _rows.AddRange(rows);

        return title;
    }

    private string BuildDebugDraw()
    {
        foreach (string channel in _draws.Channels)
        {
            _rows.Add(new OverlayRow(OverlayRow.Marked(_draws.IsEnabled(channel), channel), () => ToggleChannel(channel)));
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
            _rows.Add(new OverlayRow(OverlayRow.Marked(_scenes.Run.TimeScale == scale, label, choice: true), () => SetTimeScale(scale)));
        }

        return "Time Scale";
    }

    // Set on both. A game reads the run, and the scheduler applies it at once.
    private void SetTimeScale(double scale)
    {
        _scenes.Run.TimeScale = scale;
        _scheduler.TimeScale = scale;
        _pageStale = true;
    }

    // The scene emits first, so a run held before its first step still lists its channels.
    private void OpenDebugDraw()
    {
        _draws.Emit();
        Open(PageKind.DebugDraw);
    }

    private void Open(PageKind kind) => Open(new Page(kind, null, _cursor.Focus, null));

    private void Open(Entity subject) =>
        Open(new Page(PageKind.Entity, subject, _cursor.Focus, PanelRows.DisplayName(subject)));

    private void Open(Page page)
    {
        _pages.Add(page);
        _cursor.Reset(0);
        _pageStale = true;
    }

    private void Pop()
    {
        if (_pages.Count < 2)
        {
            return;
        }

        _cursor.Reset(_pages[^1].ReturnFocus);
        _pages.RemoveAt(_pages.Count - 1);
        _pageStale = true;
    }

    private void Exit()
    {
        _scenes.Run.RequestExit();
        StepGame(null);
    }

    // A declined or refused request is shown and not stepped. The run stays held on its scene.
    private void Request(string action, in SceneTransition transition)
    {
        try
        {
            if (!_scenes.Run.TryRequest(in transition))
            {
                SetStatus($"{action} refused: a transition is already pending");

                return;
            }
        }
        catch (Exception failure)
        {
            Report(action, failure);

            return;
        }

        StepGameReporting(action, null);
    }

    // Only a failed scene start is caught. Any other tick failure propagates.
    private void StepGameReporting(string action, Action? before)
    {
        try
        {
            StepGame(before);
        }
        catch (Exception failure) when (_scenes.TransitionFailed)
        {
            Report(action, failure);
        }
    }

    // before is a host act that runs inside the step, ahead of the scene's own work.
    private void StepGame(Action? before)
    {
        _exited = _scheduler.StepOnce(in _gameInput, _scenes, before);
        _ticksSteppedByHand += _scheduler.StepsThisFrame;
        _draws.Settle();
        _pageStale = true;
    }

    private void Report(string action, Exception failure)
    {
        Log.Error($"{action} from the debug overlay failed: {failure}");
        SetStatus($"{action} failed: {failure.GetType().Name}: {failure.Message}");
    }

    // The status line can widen the menu. The page is laid out again.
    private void SetStatus(string text)
    {
        Scene.SetStatus(text);
        _pageShown = false;
    }

    // The first frame has no interval. An Update with no Intercept before it is not a frame.
    private void SampleFrame(long gameEnd)
    {
        long frameStart = _frameStart;
        if (frameStart < 0)
        {
            return;
        }

        _frameStart = -1;
        long previous = _previousFrameStart;
        _previousFrameStart = frameStart;
        int steps = _scheduler.StepsThisFrame + _ticksSteppedByHand;
        _ticksSteppedByHand = 0;
        if (!_framePaneOn || previous < 0)
        {
            return;
        }

        Scene.Pane.Push(new FrameSample(
            Milliseconds(frameStart - previous),
            Milliseconds(gameEnd - frameStart),
            _lastFrameMs,
            steps));
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    // ReturnFocus is the row that opened the page. Name is an entity's name when opened.
    private readonly record struct Page(PageKind Kind, Entity? Subject, int ReturnFocus, string? Name);
}
