using System.Numerics;
using Capsule;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.Diagnostics;

// The orchestration layer between the game host and the overlay's own host: the switch-guarded
// button, the hold, and the debug scene it steps and draws. Knows nothing about devices or their
// backend.
internal sealed class DebugOverlay : IDisposable
{
    private readonly InputButton _button;
    private readonly FixedStepScheduler _scheduler;
    private readonly SceneHost? _scenes;
    private readonly DebugScene _debugScene;
    private readonly SimulationHost _debugHost;

    private bool _buttonDown;
    private bool _quarantined;
    private bool _open;
    private bool _disposed;

    internal DebugOverlay(InputButton button, FixedStepScheduler scheduler, SceneHost? scenes = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        _button = button;
        _scheduler = scheduler;
        _scenes = scenes;
        _debugScene = new DebugScene(SceneName(), scheduler.Tick, scheduler.StepsThisFrame);
        _debugHost = new SimulationHost(
            _debugScene,
            new InputState(new ActionBindings()),
            run: new Run
            {
                Canvas = Run.StandardCanvas,
                Sampling = TextureSampling.Point,
            });
    }

    internal bool IsOpen => _open;

    internal SimulationHost DebugHost => _debugHost;

    internal DebugScene DebugScene => _debugScene;

    // The leading edge toggles the overlay. The edge state is the raw device state, while the
    // quarantine lasts through release so a binding shared with the game cannot leak a press.
    internal DeviceSnapshot Observe(DeviceSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool down = _button.IsDown(snapshot);
        if (down && !_buttonDown)
        {
            _open = !_open;
            _scheduler.Held = _open;
            _quarantined = true;
        }

        _buttonDown = down;

        if (_quarantined)
        {
            snapshot = snapshot.Without(_button);
            if (!down)
            {
                _quarantined = false;
            }
        }

        return snapshot;
    }

    // The host calls this after the game's scheduler. The game is held while the overlay is open, so
    // this advances the overlay once on the quarantined snapshot and uses the game's current tick
    // and step count for the readout.
    internal void Step(in DeviceSnapshot snapshot, FrameRenderer renderer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_open)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(renderer);
        Refit(renderer);
        _debugScene.SetReadout(SceneName(), _scheduler.Tick, _scheduler.StepsThisFrame);
        _debugHost.Step(in snapshot);
    }

    internal void Draw(FrameRenderer renderer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_open)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(renderer);
        Refit(renderer);
        (int _, int height) = renderer.BackBufferSize;
        renderer.DrawOverlay(_debugHost.Simulation.View, ScaleFor(height));
    }

    internal string Readout
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            RefreshReadout();

            return _debugScene.Readout;
        }
    }

    void IDisposable.Dispose() => Dispose();

    internal void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debugHost.Dispose();
    }

    internal static int ScaleFor(int height) => height < 1080 ? 1 : height < 2160 ? 2 : 3;

    private void RefreshReadout() =>
        _debugScene.SetReadout(SceneName(), _scheduler.Tick, _scheduler.StepsThisFrame);

    private string SceneName() => _scenes is { } scenes ? scenes.Scene.GetType().Name : string.Empty;

    internal void Refit((int Width, int Height) backBuffer)
    {
        (int width, int height) = backBuffer;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int scale = ScaleFor(height);
        Vector2 canvas = new(width / (float)scale, height / (float)scale);
        if (_debugHost.Run.Canvas != canvas)
        {
            _debugHost.Run.Canvas = canvas;
        }
    }

    private void Refit(FrameRenderer renderer) => Refit(renderer.BackBufferSize);
}
