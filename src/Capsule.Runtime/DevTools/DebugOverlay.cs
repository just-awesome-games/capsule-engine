using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// When the menu is shown and what the game sees while it is: the toggle, the quarantine, the hold,
// and the debug scene the overlay steps and draws. Owns every data source and every action the
// menu offers; the scene knows nothing of the scheduler, the run or the registry.
internal sealed class DebugOverlay : IDisposable
{
    private readonly FixedStepScheduler _scheduler;
    private readonly ISimulation _simulation;
    private readonly SceneHost? _scenes;
    private readonly SimulationHost _host;
    private readonly ActionBindings _bindings;

    // The toggle first, then every button the menu binds; each is stripped from the game's
    // snapshot while the overlay is open and, once it is not, until the button is released.
    private readonly InputButton[] _quarantine;
    private readonly bool[] _withheld;

    private OverlayState _state;
    private bool _toggleDown;
    private bool _hideDown;

    // Set by the hide press that showed the overlay and cleared on its release: that press is
    // withheld from the menu, or the menu would read it as a fresh press and hide again.
    private bool _hidePressConsumed;

    // The frame's device state as sampled, which the menu reads, and with the menu's buttons
    // stripped, which a stepped tick reads.
    private DeviceSnapshot _sampled;
    private DeviceSnapshot _stripped;

    private bool _exited;
    private bool _disposed;

    internal DebugOverlay(
        InputButton button,
        FixedStepScheduler scheduler,
        ISimulation simulation,
        SceneHost? scenes = null,
        SceneRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(simulation);

        _scheduler = scheduler;
        _simulation = simulation;
        _scenes = scenes;
        Registrations = registry is { } registered ? registered.Registrations : [];

        Scene = new DebugScene(DebugInput.KeyName(button));
        _bindings = DebugInput.Bindings();
        _host = new SimulationHost(
            Scene,
            new InputState(_bindings),
            run: new Run
            {
                Canvas = Run.StandardCanvas,
                Sampling = TextureSampling.Point,
            });

        List<InputButton> quarantine = [button];
        foreach (InputAction action in DebugInput.Actions)
        {
            quarantine.AddRange(_bindings.ButtonsFor(action));
        }

        _quarantine = [.. quarantine];
        _withheld = new bool[_quarantine.Length];

        Scene.Push(DebugMenu.Default(this));
    }

    private enum OverlayState
    {
        Closed,
        Open,
        Hidden,
    }

    internal DebugScene Scene { get; }

    internal SimulationHost Host => _host;

    internal bool HasScenes => _scenes is not null;

    internal IReadOnlyCollection<SceneRegistration> Registrations { get; }

    internal bool IsOpen => _state == OverlayState.Open;

    internal bool IsHidden => _state == OverlayState.Hidden;

    internal string Readout
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            RefreshReadout();

            return Scene.Readout;
        }
    }

    // The host calls this before the game's scheduler with the frame's raw device state, and hands
    // the game what it returns.
    internal DeviceSnapshot Observe(DeviceSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Toggle: the leading edge, read on the raw state, takes closed to open, open to closed and
        // hidden back to open. The toggle is withheld from the menu, so a toggle that is also a menu
        // key does not fire that key's action on the frame it opens.
        InputButton toggle = _quarantine[0];
        bool wasOpen = _state == OverlayState.Open;
        bool toggleDown = toggle.IsDown(snapshot);
        if (toggleDown && !_toggleDown)
        {
            _state = wasOpen ? OverlayState.Closed : OverlayState.Open;
            _scheduler.Held = _state != OverlayState.Closed;
        }

        _toggleDown = toggleDown;
        DeviceSnapshot sampled = toggle.IsNone ? snapshot : snapshot.Without(toggle);

        // Hide: the scene is not stepped while hidden, so the edge that shows it again is read here,
        // and that press is withheld from the menu until released.
        bool hideDown = _bindings.IsAnyDown(DebugInput.Hide, snapshot);
        if (hideDown && !_hideDown && _state == OverlayState.Hidden)
        {
            _state = OverlayState.Open;
            _hidePressConsumed = true;
        }

        _hideDown = hideDown;
        _hidePressConsumed &= hideDown;
        if (_hidePressConsumed)
        {
            foreach (InputButton hide in _bindings.ButtonsFor(DebugInput.Hide))
            {
                sampled = sampled.Without(hide);
            }
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

        _stripped = snapshot;

        return snapshot;
    }

    // The host calls this after the game's scheduler. The game is held while the overlay is open,
    // so this advances the overlay once on the frame's sampled snapshot.
    internal void Step(FrameRenderer? renderer = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state != OverlayState.Open || _exited)
        {
            return;
        }

        if (renderer is not null)
        {
            Refit(renderer.BackBufferSize);
        }

        RefreshReadout();
        _host.Step(in _sampled);
    }

    internal void Draw(FrameRenderer renderer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderer);

        if (_state != OverlayState.Open || _exited)
        {
            return;
        }

        Refit(renderer.BackBufferSize);
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
    // The readout is refreshed inside the menu's own step, so the frame that stepped shows the
    // result.
    internal void StepGame()
    {
        _exited = _scheduler.StepOnce(in _stripped, _simulation);
        RefreshReadout();
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
        _host.Dispose();
    }

    private Run GameRun =>
        _scenes?.Run ?? throw new InvalidOperationException("The debug menu's scene actions need a run of scenes.");

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

            StepGame();
        }
        catch (Exception failure)
        {
            Log.Error($"{action} from the debug menu failed: {failure}");
            Scene.SetStatus($"{action} failed: {failure.GetType().Name}: {failure.Message}");
        }
    }

    private void RefreshReadout() =>
        Scene.SetReadout(_scenes is { } scenes ? scenes.Scene.GetType().Name : string.Empty, _scheduler.Tick);
}
