using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Audio;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Input;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Microsoft.Xna.Framework;

namespace Capsule.Runtime;

// The MonoGame host. Owns the window, the device and the clock, and drives the simulation on its
// own fixed-step accumulator, which makes a run reproduce frame for frame.
internal sealed class CapsuleGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly EngineBuilder _builder;
    private readonly ISimulation _simulation;

    // Null when the simulation is not a run of scenes, as in the specs.
    private readonly SceneHost? _scenes;

    private readonly InputConfiguration _configuration;
    private readonly MouseSampler _mouse = new();
    private readonly GamepadSampler _pad = new();
    private readonly FixedStepScheduler _scheduler;

    // Null when the simulation is not a run of scenes, which has no rumble to apply.
    private readonly GamepadRumble? _rumble;

    // Null unless the builder opted in, and owned by the builder. The frame path guards every use
    // with a null check.
    private readonly FrameDiagnostics? _diagnostics;

    // The host reaches the overlay through delegates built in the guarded block, which lets a shipping
    // publish trim the overlay's methods away.
    private readonly Func<DeviceSnapshot, FrameRenderer, DeviceSnapshot>? _observeOverlay;
    private readonly Action<FrameRenderer>? _stepOverlay;
    private readonly Action<FrameRenderer>? _drawOverlay;
    private readonly Action<AudioPlayer>? _followOverlayHold;
    private readonly IDisposable? _overlayHost;

    private TextureStore _textures = null!;
    private FrameRenderer _renderer = null!;

    // All null when the sound device would not open, which makes every command and preload a no-op
    // instead of failing the run. The store disposes the device.
    private SoundDevice? _device;
    private SoundStore? _sounds;
    private AudioPlayer? _audio;

    private float _lastOutputGain = -1f;

    // Null until the renderer exists, and null for a platform with no redraw watch.
    private IDisposable? _redrawWatch;

    private bool _windowRaised;
    private bool _deviceSeeded;

    // Whether the host is inside a device operation of its own. The resize watch fires for the
    // window events such an operation raises, so it stands off instead of reaching a half-applied
    // device or recursing.
    private bool _deviceHeld;
    private bool _fullscreenChordHeld;
    private bool _fullscreenChordQuarantined;

    internal CapsuleGame(EngineBuilder builder, ISimulation simulation, SceneHost? scenes, FrameDiagnostics? diagnostics)
    {
        _builder = builder;
        _diagnostics = diagnostics;
        _simulation = simulation;
        _scenes = scenes;
        _configuration = builder.Input;
        _scheduler = new FixedStepScheduler(builder.StepSeconds, builder.MaxStepsPerFrame, builder.Input.Bindings, builder.Driver, scenes);
        _rumble = scenes is null ? null : new GamepadRumble(GamepadRumble.WriteToPad);

        if (Development.IsSupported)
        {
            OverlayHost overlay = new(builder.Input.DebugMenuButton, _scheduler, simulation, scenes, builder.Scenes);
            _overlayHost = overlay;
            _observeOverlay = (snapshot, renderer) => overlay.Observe(
                snapshot,
                renderer.ScreenLayer,
                OverlayHost.ScaleFor(renderer.BackBufferSize.Height));
            _stepOverlay = renderer => overlay.Step(renderer);
            _drawOverlay = overlay.Draw;
            // The subscription is built here so the audio player's suspension is reachable only
            // through this block, and a shipping publish trims it with the overlay.
            _followOverlayHold = audio => overlay.HoldChanged = held =>
            {
                if (held)
                {
                    audio.Suspend();
                }
                else
                {
                    audio.Resume();
                }
            };
        }

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = builder.WindowWidth,
            PreferredBackBufferHeight = builder.WindowHeight,
            // Borderless. The preferred size is ignored while fullscreen, so leaving fullscreen
            // restores the configured window.
            HardwareModeSwitch = false,
            IsFullScreen = builder.Fullscreen,
        };

        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.Title = builder.WindowTitle;
        Window.AllowUserResizing = builder.Resizable;

        _diagnostics?.Mark(FrameDiagnostics.Stage.HostConstructed);
    }

    internal long SimulationTick => _scheduler.Tick;

    protected override void Initialize()
    {
        // The platform, the window and the device are up by here. base.Initialize loads content.
        _diagnostics?.Mark(FrameDiagnostics.Stage.DeviceReady);

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _textures = new TextureStore(GraphicsDevice, _builder.Platform);

        if (SoundDevice.TryOpen(_builder.Platform) is { } device)
        {
            _device = device;
            _sounds = new SoundStore(device);
            _audio = new AudioPlayer(_sounds);
        }

        if (_scenes is { } scenes)
        {
            scenes.PrepareAssets = PrepareAssets;
            scenes.PrefetchAssets = PrefetchAssets;
            PrepareAssets(scenes.Scene.CollectAssetPreloads());

            if (_audio is { } audio)
            {
                // The overlay's hold is a standstill, and the ear should hear it as one.
                _followOverlayHold?.Invoke(audio);

                // Per step, because the mixer rewrites its commands every step and a frame may run
                // several. The flush after the step that requests exit persists that step's writes.
                _scheduler.StepCompleted = () =>
                {
                    audio.Apply(scenes.Run.Audio.Commands);
                    scenes.FlushSaves();
                };

                // The initial scene started before the device existed, so what its start raised is
                // still on the mixer and the first BeginStep would clear it unheard. A later scene
                // starts inside the step that asked for it and is delivered with that step's
                // commands.
                audio.Apply(scenes.Run.Audio.Commands);
            }
            else
            {
                _scheduler.StepCompleted = scenes.FlushSaves;
            }
        }

        _diagnostics?.Mark(FrameDiagnostics.Stage.SceneAssetsLoaded);
        _renderer = new FrameRenderer(GraphicsDevice, _builder.RenderResolution, _textures);

        // Update samples the mouse before the first Draw places the layer, so the mapping is settled
        // here and the first step reads a canvas position.
        _renderer.ResolveScreenLayer(_simulation.View);

        // Installed once the renderer exists, since the watch can fire before the next frame does.
        _redrawWatch = _builder.Platform.WatchWindowRedraw(new WindowHandle(Window.Handle), RedrawWindow);

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        _diagnostics?.BeginUpdate();

        // Sampled every frame, including one that drains no step, and the latch carries that frame's
        // input to the step that eventually runs. The pointer is mapped through the screen layer's
        // placement, so it reaches the simulation as a canvas position. IsActive is unusable here
        // because it reads true before focus is granted.
        bool active = _builder.Platform.HasInputFocus(new WindowHandle(Window.Handle));
        PadFilter padFilter = new(_configuration.StickDeadzone, _configuration.TriggerDeadzone);
        DeviceSnapshot sampled = _mouse.SampleOnto(
            _pad.SampleOnto(KeyboardSampler.Sample(), padFilter),
            _renderer.ScreenLayer,
            active);

        // The first sample decides the run's initial active device: a pad found before the first step
        // seeds Gamepad. A driven run seeds the keyboard, as a headless one does, and the driver's
        // snapshots move it from there.
        if (!_deviceSeeded)
        {
            _deviceSeeded = true;
            _scheduler.SeedDevice(_builder.Driver is null && _pad.IsConnected ? InputDevice.Gamepad : InputDevice.KeyboardMouse);
        }

        // Alt+Enter belongs to the host and is not bindable. It is withheld for the whole gesture,
        // or a game that binds Enter reads a press out of it.
        if (ConsumeFullscreenChord(sampled))
        {
            sampled = sampled.Without(Key.Enter).Without(Key.LeftAlt).Without(Key.RightAlt);
        }

        if (_observeOverlay is { } observe)
        {
            sampled = observe(sampled, _renderer);
        }

        // The run owns the pace and the scheduler holds what is applied. Copied after the overlay's
        // observe, which may move it, and before the frame's elapsed time is spent.
        if (_scenes is { } paced)
        {
            _scheduler.TimeScale = paced.Run.TimeScale;
        }

        _scheduler.Output = new System.Numerics.Vector2(GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);
        bool exiting = _scheduler.Advance(gameTime.ElapsedGameTime.TotalSeconds, sampled, _simulation);

        _stepOverlay?.Invoke(_renderer);

        // After the steps that may have asked for a prefetch.
        _textures.Pump();
        _sounds?.Pump();

        // Every frame, including one that drained no step. The device follows the system's default
        // output, and a streamed voice hands it the next buffers.
        _device?.Update(gameTime.ElapsedGameTime.TotalSeconds);

        if (_device is { } device && _scenes is { } scenes)
        {
            float gain = active ? 1f : scenes.Run.Audio.UnfocusedVolume;
            if (gain != _lastOutputGain)
            {
                device.SetOutputGain(gain);
                _lastOutputGain = gain;
            }
        }

        // Every frame, after the steps: the level is the run's settled output, and focus and the pad's
        // slot are the host's. The applier rests the motors while the window is inactive and rewrites
        // the level when focus returns, and the simulation sees neither.
        if (_rumble is { } rumble && _scenes is { } rumbled)
        {
            rumble.Apply(
                rumbled.Run.Rumble.Level,
                active,
                _pad.IsConnected,
                _scheduler.ActiveDevice == InputDevice.Gamepad,
                _pad.ConnectedPlayer,
                gameTime.ElapsedGameTime.TotalSeconds);
        }

        _audio?.Update();

        if (exiting)
        {
            // The motors are rested before the window goes, and Dispose repeats it harmlessly.
            _rumble?.Silence();
            Exit();
        }

        base.Update(gameTime);

        _diagnostics?.EndUpdate(_scheduler.StepsThisFrame);
    }

    protected override void Draw(GameTime gameTime)
    {
        // The first draw is the first tick after the backend shows the window. A launch from a
        // terminal leaves the window behind the terminal and deaf to input until it is raised.
        if (!_windowRaised)
        {
            _windowRaised = true;
            _builder.Platform.RaiseWindow(new WindowHandle(Window.Handle));
        }

        _diagnostics?.BeginDraw();

        // alpha is in [0, 1) because Update drains the accumulator below one step.
        _renderer.Draw(_simulation.View, _scheduler.InterpolationAlpha);

        // The diagnostics cover the game frame's submission. The capture, the overlay and the
        // present are excluded, and the present's vsync wait runs in Game.Tick after this returns.
        bool budgetSpent = _diagnostics?.EndDraw() ?? false;

        // Taken while the surface still holds the frame, ahead of the present. A request raised
        // while the window is minimised stands until a frame draws.
        if (_scenes is { } scenes && _renderer.CanCaptureFrame && scenes.TryTakeFrameCapture(out string capturePath))
        {
            _renderer.SaveSurface(capturePath);
        }

        _drawOverlay?.Invoke(_renderer);

        base.Draw(gameTime);

        if (budgetSpent)
        {
            _rumble?.Silence();
            Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // First, and on every path out of the host: a crash disposes the host before the crash
            // log is written, and a pad left buzzing is what the player would notice.
            _rumble?.Silence();

            _overlayHost?.Dispose();

            // Disposed ahead of the renderer, which the watch draws through.
            _redrawWatch?.Dispose();
            _redrawWatch = null;

            // The scheduler outlives this host, and the mixer it fed commands from is the run's.
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

    // What a scene boundary loads. The stores are exchanged together, and a failure leaves the run
    // on its current scene.
    private void PrepareAssets(AssetCollection preloads)
    {
        _textures.ChangeScene(preloads, () => _sounds?.ChangeScene(preloads));
    }

    private void PrefetchAssets(AssetCollection preloads)
    {
        _textures.Prefetch(preloads);
        _sounds?.Prefetch(preloads);
    }

    // Draws the settled frame again at the window's current extent, from inside SDL's own event
    // handling. Windows blocks the game loop for the length of a window drag, so this is where the
    // view refits while the edge moves. No step runs and no capture is taken, because a drag
    // advances no simulation time.
    private void RedrawWindow(int width, int height)
    {
        // The frame loop owns the device between frames, and applying the new extent raises the
        // window events this watches for. The platform reads the extent at the call, and an event this
        // guard drops costs a frame, not the fit.
        if (_deviceHeld || !_windowRaised)
        {
            return;
        }

        _deviceHeld = true;

        try
        {
            // The preferred extent is the windowed one, while fullscreen uses the desktop's.
            // Writing the monitor's extent here would make it the window Alt+Enter returns to. A
            // fullscreen transition still changes the fit, so the frame is redrawn.
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

            _drawOverlay?.Invoke(_renderer);

            GraphicsDevice.Present();
        }
        finally
        {
            _deviceHeld = false;
        }
    }

    // Enters or leaves borderless fullscreen. The preferred back buffer is left alone, since it
    // holds the windowed extent that leaving fullscreen restores.
    private void ToggleFullscreen()
    {
        // Held across the transition, which raises the window events the resize watch answers. A
        // redraw landing inside it would reach a half-applied device.
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

    // Toggles the window on the chord's leading edge. Returns whether Alt and Enter are still
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

        // The quarantine ends once both keys are up. Releasing one first would hand the other to
        // the simulation as a fresh press.
        _fullscreenChordQuarantined = held || (_fullscreenChordQuarantined && (alt || enter));

        return _fullscreenChordQuarantined;
    }
}
