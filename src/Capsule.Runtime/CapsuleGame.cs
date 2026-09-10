using Capsule.Assets;
using Capsule.Input;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Input;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Microsoft.Xna.Framework;

namespace Capsule.Runtime;

// The MonoGame host. Owns the window, the device and the clock, and drives the simulation on its
// own fixed-step accumulator rather than MonoGame's, so a run reproduces frame for frame.
internal sealed class CapsuleGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly EngineOptions _options;
    private readonly ISimulation _simulation;

    // Null when the simulation is not a run of scenes, which is the specs' case.
    private readonly SceneHost? _scenes;

    private readonly PadFilter _padFilter;
    private readonly FixedStepScheduler _scheduler;

    // Null unless the builder opted in, and owned by it: every use on the frame path is that
    // null check.
    private readonly FrameDiagnostics? _diagnostics;

    private TextureStore _textures = null!;
    private FrameRenderer _renderer = null!;

    // Both null when the sound device would not open, which leaves every command and every preload
    // a no-op rather than failing the run.
    private SoundStore? _sounds;
    private AudioPlayer? _audio;

    private bool _windowRaised;

    // Whether the host is inside a device operation of its own. The resize watch fires for the
    // window events that operation raises, so it stands off rather than reaching a device that is
    // half-applied — and rather than recursing into itself.
    private bool _deviceHeld;
    private bool _fullscreenChordHeld;
    private bool _fullscreenChordQuarantined;

    internal CapsuleGame(EngineOptions options, ISimulation simulation, SceneHost? scenes, FrameDiagnostics? diagnostics)
    {
        _options = options;
        _diagnostics = diagnostics;
        _simulation = simulation;
        _scenes = scenes;
        _padFilter = new PadFilter(options.Input.StickDeadzone, options.Input.TriggerDeadzone);
        _scheduler = new FixedStepScheduler(options.StepSeconds, options.MaxStepsPerFrame, options.Input.Bindings, options.Driver, scenes);

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = options.WindowWidth,
            PreferredBackBufferHeight = options.WindowHeight,
            // Borderless; the preferred size is ignored while fullscreen, so leaving fullscreen
            // restores the configured window with no work here.
            HardwareModeSwitch = false,
            IsFullScreen = options.Fullscreen,
        };

        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.Title = options.WindowTitle;
        Window.AllowUserResizing = options.Resizable;

        _diagnostics?.Mark(FrameDiagnostics.Stage.HostConstructed);
    }

    internal long SimulationTick => _scheduler.Tick;

    protected override void Initialize()
    {
        // The platform, the window and the device are up by here; base.Initialize loads content.
        _diagnostics?.Mark(FrameDiagnostics.Stage.DeviceReady);

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _textures = new TextureStore(GraphicsDevice);

        if (SoundDevice.TryOpen() is { } device)
        {
            _sounds = new SoundStore(device);
            _audio = new AudioPlayer(_sounds);
        }

        if (_scenes is { } scenes)
        {
            scenes.PrepareAssets = PrepareAssets;
            PrepareAssets(scenes.Scene.CollectAssetPreloads());

            if (_audio is { } audio)
            {
                // Per step, not per frame: the mixer rewrites its commands every step and a frame
                // may run several.
                _scheduler.StepCompleted = () => audio.Apply(scenes.Audio.Commands);

                // The initial scene started before the device existed, so what its start raised is
                // still on the mixer; the first step's BeginStep would clear it unheard. A later
                // scene starts inside the step that asked for it, so its start is delivered with
                // that step's commands.
                audio.Apply(scenes.Audio.Commands);
            }
        }

        _diagnostics?.Mark(FrameDiagnostics.Stage.SceneAssetsLoaded);
        _renderer = new FrameRenderer(GraphicsDevice, _options.RenderResolution, _textures);

        // Installed once the renderer exists, since the watch can fire before the next frame does.
        SdlPlatform.WatchWindowRedraw(RedrawWindow);

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        _diagnostics?.BeginUpdate();

        // Sampled every frame including one that drains no step; the latch carries that frame's
        // input to the step that eventually runs.
        DeviceSnapshot sampled = GamepadSampler.SampleOnto(KeyboardSampler.Sample(), _padFilter);

        // Alt+Enter is the host's, never a bindable action. Withheld for the whole gesture, or a
        // game that binds Enter reads a press out of it.
        if (ConsumeFullscreenChord(sampled))
        {
            sampled = sampled.Without(Key.Enter).Without(Key.LeftAlt).Without(Key.RightAlt);
        }

        bool exiting = _scheduler.Advance(gameTime.ElapsedGameTime.TotalSeconds, sampled, _simulation);

        // Every frame, including one that drained no step: a streamed voice hands the device its
        // next buffers here, and base.Update is what services them.
        _audio?.Update();

        if (exiting)
        {
            Exit();
        }

        base.Update(gameTime);

        _diagnostics?.EndUpdate();
    }

    protected override void Draw(GameTime gameTime)
    {
        // The first draw is the first tick after the backend shows the window, which is where a
        // launch from a terminal would otherwise leave the game behind it and deaf to input.
        if (!_windowRaised)
        {
            _windowRaised = true;
            SdlPlatform.RaiseWindow(Window.Handle);
        }

        _diagnostics?.BeginDraw();

        // alpha is in [0, 1) because Update drains the accumulator below one step.
        _renderer.Draw(_simulation.View, _scheduler.InterpolationAlpha);

        // While the surface still holds the frame, ahead of the present. The request is taken only
        // once a frame has drawn, so one raised while the window is minimised stands until one does.
        if (_scenes is { } scenes && _renderer.CanCaptureFrame && scenes.TryTakeFrameCapture(out string capturePath))
        {
            _renderer.SaveSurface(capturePath);
        }

        base.Draw(gameTime);

        // Present is not inside the measured section: Game.Tick calls EndDraw after this returns,
        // and the vsync wait lives there. The diagnostics cover render submission only.
        if (_diagnostics is not null && _diagnostics.EndDraw())
        {
            Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Ahead of the renderer: the watch draws through it.
            SdlPlatform.StopWatchingWindowRedraw();

            // The scheduler outlives this, and the mixer it fed commands from is the run's.
            _scheduler.StepCompleted = null;

            // Voices before the sounds they play and the device that opened them.
            _audio?.Dispose();
            _sounds?.Dispose();

            // Null when construction failed before LoadContent ran.
            _renderer?.Dispose();
            _textures?.Dispose();
        }

        base.Dispose(disposing);
    }

    // The whole of what a scene boundary loads: every store is exchanged together, and one that
    // fails leaves the run on the scene it was on.
    private void PrepareAssets(AssetCollection preloads)
    {
        _textures.ChangeScene(preloads);
        _sounds?.ChangeScene(preloads);
    }

    // Draws the settled frame again at the window's current extent, from inside SDL's own event
    // handling. Windows blocks the game loop for the whole of a window drag, so this is the only
    // point the view can refit while the edge is moving. No step runs and no capture is taken: a
    // drag advances no simulation time and produces no frame the game asked for.
    private void RedrawWindow()
    {
        // The device is only the frame loop's between frames, and applying the new extent raises
        // the very window events this is watching for. The extent is read fresh below rather than
        // carried on the event, so an event this guard drops costs a frame and not the fit.
        if (_deviceHeld || !_windowRaised)
        {
            return;
        }

        _deviceHeld = true;

        try
        {
            SdlPlatform.WindowSize(Window.Handle, out int width, out int height);

            // The preferred extent is the windowed one, and fullscreen is the desktop's: writing
            // the monitor's extent into it would make that the window Alt+Enter returns to. A
            // fullscreen transition changes the fit, so the frame is still redrawn.
            if (!_graphics.IsFullScreen
                && width > 0
                && height > 0
                && (_graphics.PreferredBackBufferWidth != width || _graphics.PreferredBackBufferHeight != height))
            {
                _graphics.PreferredBackBufferWidth = width;
                _graphics.PreferredBackBufferHeight = height;
                _graphics.ApplyChanges();
            }

            _renderer.Draw(_simulation.View, _scheduler.InterpolationAlpha);
            GraphicsDevice.Present();
        }
        finally
        {
            _deviceHeld = false;
        }
    }

    // Enters or leaves borderless fullscreen. The preferred back buffer is left alone throughout:
    // it is the windowed extent, which is what leaving fullscreen restores.
    private void ToggleFullscreen()
    {
        // Held across the whole transition: it raises the window events the resize watch answers,
        // and a redraw landing inside it would reach a half-applied device.
        _deviceHeld = true;

        try
        {
            _graphics.IsFullScreen = !_graphics.IsFullScreen;
            _graphics.ApplyChanges();
        }
        finally
        {
            _deviceHeld = false;
        }
    }

    // Toggles the window on the chord's leading edge; returns whether Alt and Enter are still
    // quarantined from the simulation.
    private bool ConsumeFullscreenChord(in DeviceSnapshot snapshot)
    {
        bool alt = snapshot.IsDown(Key.LeftAlt) || snapshot.IsDown(Key.RightAlt);
        bool enter = snapshot.IsDown(Key.Enter);
        bool held = alt && enter;

        if (held && !_fullscreenChordHeld)
        {
            ToggleFullscreen();
        }

        _fullscreenChordHeld = held;

        // The quarantine outlives the chord, ending only once both keys are up: releasing one
        // first would otherwise hand the other to the simulation as a fresh press.
        _fullscreenChordQuarantined = held || (_fullscreenChordQuarantined && (alt || enter));

        return _fullscreenChordQuarantined;
    }
}
