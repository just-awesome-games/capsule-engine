using System.Diagnostics;
using System.Runtime;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Audio;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Gpu;
using Capsule.Runtime.Input;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime;

// The MonoGame host. Owns the window, the device and the clock, and drives the scenes on its own
// fixed-step accumulator, which makes a run reproduce frame for frame.
internal sealed class CapsuleGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly EngineBuilder _builder;
    private readonly SceneHost _scenes;
    private readonly MouseSampler _mouse = new();
    private readonly FixedStepScheduler _scheduler;
    private readonly GamepadRumble _rumble = new(GamepadRumble.WriteToPad);
    private readonly FramePacer _pacer = new();

    // What the player's keyboard layout prints on each key, indexed by key, which the run's input
    // state reads key characters from. Read at load and rewritten in place on a layout change.
    private readonly string?[] _keyLayout = new string?[KeyboardSampler.KeyCount];

    // Null unless the builder opted in. The builder owns it.
    private readonly FrameDiagnostics? _diagnostics;

    // The host reaches the overlay through delegates built in the guarded block, which lets a shipping
    // publish trim the overlay's methods away.
    private readonly Func<DeviceSnapshot, FrameRenderer, DeviceSnapshot>? _interceptOverlay;
    private readonly Action<FrameRenderer>? _updateOverlay;
    private readonly Action<FrameRenderer>? _drawOverlay;
    private readonly Action<SoundDevice>? _attachOverlaySound;
    private readonly Func<bool>? _overlayHeld;
    private readonly IDisposable? _overlayHost;

    // The latency measurement's three moments, built in the same guarded block as the overlay's and
    // null unless the builder asked for one.
    private readonly Action<InputEvents>? _latencyTaken;
    private readonly Action<int>? _latencyStepped;
    private readonly Action? _latencyPresented;
    private readonly IDisposable? _latency;

    // Null until LoadContent. Dispose can run without it after a failed construction.
    private GpuDevice _gpu = null!;
    private TextureStore _textures = null!;
    private ShaderStore _shaders = null!;
    private FrameRenderer _renderer = null!;
    private CursorApplier _cursor = null!;
    private InputFeed _input = null!;
    private GamepadSampler _pad = null!;

    // All null when the platform opened no audio output, which makes every command and preload a
    // no-op instead of failing the run. The device owns the store and the player.
    private SoundDevice? _device;
    private SoundStore? _sounds;
    private AudioPlayer? _audio;

    // Null until the renderer exists, and null for a platform with no redraw watch.
    private IDisposable? _redrawWatch;

    private bool _windowRaised;
    private bool _focusSeen;
    private bool _deviceSeeded;
    private bool _minimized;

    // When the current frame started, from which the next frame's elapsed time is measured.
    private long _frameStarted;

    // The run's display as the window last applied it.
    private WindowRequest _applied;

    // Whether the host is inside a device operation of its own. The resize watch fires for the
    // window events such an operation raises. It stands off instead of reaching a half-applied
    // device or recursing.
    private bool _deviceHeld;

    internal CapsuleGame(EngineBuilder builder, SceneHost scenes, FrameDiagnostics? diagnostics)
    {
        _builder = builder;
        _diagnostics = diagnostics;
        _scenes = scenes;
        _scheduler = new FixedStepScheduler(builder.StepSeconds, builder.MaxStepsPerFrame, builder.Input.Bindings, builder.Driver, scenes);

        if (Development.IsSupported && builder.InputLatencyPath is { } latencyPath)
        {
            InputLatency latency = new(latencyPath);
            _latency = latency;
            _latencyTaken = events => latency.Take(events.PressTimes);
            _latencyStepped = latency.Stepped;
            _latencyPresented = latency.Presented;
        }

        if (Development.IsSupported)
        {
            DebugOverlay overlay = new(builder.Input.DebugOverlayButton, _scheduler, scenes, builder.Scenes);
            _overlayHost = overlay;
            _interceptOverlay = (snapshot, renderer) => overlay.Intercept(
                snapshot,
                renderer.ScreenLayer,
                DebugOverlay.ScaleFor(renderer.BackBufferSize.Height),
                renderer.WorldPlacement);
            _updateOverlay = renderer => overlay.Update(renderer);
            _drawOverlay = overlay.Draw;
            _overlayHeld = () => overlay.IsOpen || overlay.IsHidden;
            // Built here so the sound device is reachable from the overlay only through this block, and
            // a shipping publish trims it with the overlay.
            _attachOverlaySound = device =>
            {
                overlay.Scene.Pane.Sound = device;
                overlay.HoldChanged = held =>
                {
                    if (held)
                    {
                        device.Player.Suspend();
                    }
                    else
                    {
                        device.Player.Resume();
                    }
                };
            };
        }

        // The run has started by now, and the window opens as its start left the display.
        Display display = scenes.Run.Display;
        _applied = new WindowRequest(display.Fullscreen, display.WindowSize, display.VSync);
        (int displayWidth, int displayHeight, float displayScale) = builder.Platform.WindowDisplay(new WindowHandle(Window.Handle));
        (int width, int height) = display.WindowPixels(displayWidth, displayHeight, displayScale);
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = width,
            PreferredBackBufferHeight = height,
            // Borderless. The preferred size is ignored while fullscreen. Leaving fullscreen
            // restores the windowed size.
            HardwareModeSwitch = false,
            IsFullScreen = display.Fullscreen,
            SynchronizeWithVerticalRetrace = display.VSync && !builder.Uncapped,
        };

        // The frame pacer owns every wait, focused or not.
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero;
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
        _gpu = new GpuDevice(GraphicsDevice);
        TextureSamplers samplers = new(_gpu);
        _textures = new TextureStore(_gpu, samplers, _builder.Platform);
        _shaders = new ShaderStore(_gpu, _builder.Platform, FrameRenderer.SurfaceFormat, _gpu.SwapchainTextureFormat);

        if (SoundDevice.TryOpen(_builder.Platform) is { } device)
        {
            _device = device;
            _sounds = device.Sounds;
            _audio = device.Player;
        }

        SceneHost scenes = _scenes;
        scenes.PrepareAssets = PrepareAssets;
        scenes.PrefetchAssets = PrefetchAssets;
        scenes.SceneEntered = CollectAtBoundary;
        scenes.PrepareInitialAssets();
        CollectAtBoundary();

        if (_device is { } sound)
        {
            // The overlay's hold is a standstill, and the ear should hear it as one. Its frame pane
            // counts the mix's gaps.
            _attachOverlaySound?.Invoke(sound);
        }

        scenes.FollowSteps(_scheduler, _audio);

        _diagnostics?.Mark(FrameDiagnostics.Stage.SceneAssetsLoaded);
        _renderer = new FrameRenderer(_gpu, samplers, _builder.RenderResolution, _textures, _shaders);
        _cursor = new CursorApplier(
            _textures,
            confined => _builder.Platform.ConfineCursor(new WindowHandle(Window.Handle), confined));

        // The watch first, and then the pads already connected, which raised their added events before
        // it existed. A pad plugged in between the two is opened once.
        _input = new InputFeed();
        _input.UseTriggerDeadzone(_builder.Input.TriggerDeadzone);
        _pad = new GamepadSampler(_builder.Platform.OpenContent);

        // The layout gives key characters from the first step, and the feed reports each later change.
        KeyboardSampler.ReadLayout(_keyLayout);
        _scheduler.UseKeyLayout(_keyLayout);

        // Update samples the mouse before the first Draw places the layer. The mapping is settled
        // here and the first step reads a canvas position.
        _renderer.ResolveScreenLayer(scenes.View);

        // Installed once the renderer exists, since the watch can fire before the next frame does.
        _redrawWatch = _builder.Platform.WatchWindowRedraw(new WindowHandle(Window.Handle), RedrawWindow);

        base.LoadContent();
    }

    protected override void BeginRun()
    {
        _frameStarted = Stopwatch.GetTimestamp();

        base.BeginRun();
    }

    protected override void Update(GameTime gameTime)
    {
        // The backend pumped SDL's events just before this. The frame starts at its input, and the
        // capped wait has already passed at the end of the last frame.
        long frameStarted = Stopwatch.GetTimestamp();
        double elapsedSeconds = (double)(frameStarted - _frameStarted) / Stopwatch.Frequency;
        _frameStarted = frameStarted;

        _diagnostics?.BeginUpdate();

        // Sampled every frame, including one that drains no step, and the latch carries that frame's
        // input to the step that eventually runs. The pointer is mapped through the screen layer's
        // placement. It reaches the simulation as a canvas position. IsActive is unusable here
        // because it reads true before focus is granted. An inactive window reads no device. The
        // presses since the last sample are taken either way, and an inactive window drops them.
        bool active = _builder.Platform.HasInputFocus(new WindowHandle(Window.Handle));
        InputEvents events = _input.Take();
        if (active)
        {
            _latencyTaken?.Invoke(events);
        }

        if (events.KeymapChanged)
        {
            KeyboardSampler.ReadLayout(_keyLayout);
        }

        DeviceSnapshot sampled = active ? KeyboardSampler.Sample(events) : DeviceSnapshot.Empty;
        sampled = _pad.SampleOnto(sampled, new PadFilter(_builder.Input.StickDeadzone, _builder.Input.TriggerDeadzone), active, events);
        sampled = _mouse.SampleOnto(sampled, _renderer.ScreenLayer, active, events);

        // A window that has never had focus has not lost it. The frames before the first Draw raises
        // the window read focused, and a launch from a terminal raises no loss.
        _focusSeen |= active;
        sampled = sampled.WithWindowFocus(active || !_focusSeen);

        // The first sample decides the run's initial active device: a pad found before the first step
        // seeds Gamepad. A driven run seeds the keyboard, as a headless one does, and the driver's
        // snapshots move it from there.
        if (!_deviceSeeded)
        {
            _deviceSeeded = true;
            _scheduler.SeedDevice(_builder.Driver is null && _pad.ActivePad != 0 ? InputDevice.Gamepad : InputDevice.KeyboardMouse);
        }

        if (_interceptOverlay is { } intercept)
        {
            sampled = intercept(sampled, _renderer);
        }

        // The run owns the pace and the scheduler holds what is applied. Copied after the overlay's
        // intercept, which may move it, and before the frame's elapsed time is spent.
        _scheduler.TimeScale = _scenes.Run.TimeScale;
        _scheduler.Output = new System.Numerics.Vector2(GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);
        bool exiting = _scheduler.Advance(elapsedSeconds, sampled, _scenes);

        // After the steps, which may have changed the trigger deadzone, and before the backend's next
        // pump. The watch then reads the presses that pump delivers against the deadzone the run settled.
        _input.UseTriggerDeadzone(_builder.Input.TriggerDeadzone);

        _latencyStepped?.Invoke(_scheduler.StepsThisFrame);

        _updateOverlay?.Invoke(_renderer);

        // After the steps that may have asked for a prefetch. Texture pages upload in the frame's own
        // copy pass.
        _sounds?.Pump();

        // Every frame, including one that drained no step. The voices the mix thread has ended are
        // retired, a fault on the mix or decode threads is logged, and a lost output is reopened.
        _device?.Update(elapsedSeconds);

        // Every frame, after the steps: the level is the run's settled output, and the live window
        // focus, the hold and the active pad are the host's. The applier rests the motors while the
        // window is inactive or the run is held, and rewrites the level when either ends. A driven
        // run's scripted window focus does not reach them.
        _rumble.Apply(
            _scenes.Run.Rumble.Level,
            active,
            _scheduler.ActiveDevice == InputDevice.Gamepad,
            _scheduler.Held,
            _pad.ActivePad,
            elapsedSeconds);

        // Every frame, after the steps. The pointer follows the settled run and the overlay.
        _cursor.Apply(
            _scenes.Run.Cursor,
            _scheduler.ActiveDevice == InputDevice.Gamepad,
            _overlayHeld?.Invoke() ?? false,
            _renderer.ScreenLayer.Scale);

        // Every frame, after the steps. The window follows the settled display, and the next frame
        // waits the period it asks for now.
        _minimized = _builder.Platform.IsMinimized(new WindowHandle(Window.Handle));
        ApplyDisplay(_scenes.Run.Display);
        _scheduler.FramePeriodSeconds = FramePacer.PeriodSeconds(_scenes.Run.Display, active, _minimized, _builder.StepSeconds, _builder.Uncapped);

        // A drawn frame ends a timed diagnostics run in Draw. A minimized one never reaches it.
        exiting |= _minimized && _diagnostics is { Expired: true };

        if (exiting)
        {
            // The motors are rested before the window goes, and Dispose repeats it harmlessly.
            _rumble.Silence();
            Exit();
        }

        base.Update(gameTime);

        _diagnostics?.EndUpdate(_scheduler.StepsThisFrame);
    }

    // A minimized window shows nothing. Its frames build and draw nothing, and wait here instead of
    // after a present.
    protected override bool BeginDraw()
    {
        if (!_minimized && base.BeginDraw())
        {
            return true;
        }

        _pacer.Wait(_scheduler.FramePeriodSeconds);
        return false;
    }

    // The present returns inside the backend's EndDraw. The frame cap's wait follows it, once a frame,
    // and the backend pumps SDL's events after the wait. The next frame then samples what the player
    // did while it waited. The period is the one this frame settled.
    protected override void EndDraw()
    {
        base.EndDraw();

        _latencyPresented?.Invoke();
        _pacer.Wait(_scheduler.FramePeriodSeconds);
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

        // The first read since the last step builds the view, timed apart from the submission.
        _diagnostics?.BeginView();
        Capsule.Rendering.FrameView view = _scenes.View;

        _diagnostics?.BeginDraw();

        // alpha is in [0, 1) because Update drains the accumulator below one step.
        _renderer.Draw(view, _scheduler.InterpolationAlpha);

        // The diagnostics cover the game frame's submission. The capture, the overlay and the
        // present are excluded, and the present's vsync wait runs in Game.Tick after this returns.
        bool budgetSpent = _diagnostics?.EndDraw() ?? false;

        // Taken while the surface still holds the frame, ahead of the present. A request raised
        // while the window is minimised, or while a stand-in camera frames the view, stands until the
        // game's own frame draws.
        if (_renderer.CanCaptureFrame && _scenes.ViewCamera is null && _scenes.TryTakeFrameCapture(out string capturePath))
        {
            _renderer.SaveSurface(capturePath);
        }

        _drawOverlay?.Invoke(_renderer);
        _renderer.Submit();

        base.Draw(gameTime);

        if (budgetSpent)
        {
            _rumble.Silence();
            Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // First, and on every path out of the host: a crash disposes the host before the crash
            // log is written, and a pad left buzzing is what the player would notice.
            _rumble.Silence();

            _overlayHost?.Dispose();
            _latency?.Dispose();
            _cursor?.Dispose();

            // After the motors rest, which writes to the open pads.
            _pad?.Dispose();
            _input?.Dispose();

            // Disposed ahead of the renderer, which the watch draws through.
            _redrawWatch?.Dispose();
            _redrawWatch = null;

            // The scheduler outlives this host, and the mixer it fed commands from is the run's.
            _scheduler.StepCompleted = null;
            _scenes.BoundaryReached = null;

            // The mix thread stops before the output, the clips and the decoders it reads go.
            _device?.Dispose();

            _renderer?.Dispose();
            _shaders?.Dispose();
            _textures?.Dispose();
            _gpu?.Dispose();
        }

        base.Dispose(disposing);
    }

    // What a scene boundary loads. The stores are exchanged together, and a failure leaves the run
    // on its current scene.
    private void PrepareAssets(AssetCollection preloads)
    {
        _textures.ChangeScene(preloads, () => _sounds?.ChangeScene(preloads));
        _shaders.Load(preloads.Shaders);
    }

    // Gameplay starts on an empty young generation whatever the machine's GC budget is.
    private static void CollectAtBoundary()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private void PrefetchAssets(AssetCollection preloads)
    {
        _textures.Prefetch(preloads);
        _sounds?.Prefetch(preloads);
        _shaders.Load(preloads.Shaders);
    }

    // Draws the settled frame again at the window's current extent, from inside SDL's own event
    // handling. Windows blocks the game loop for the length of a window drag. This is where the
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
            // Writing the monitor's extent here would make it the window leaving fullscreen returns
            // to. A fullscreen transition still changes the fit. The frame is redrawn.
            if (!_graphics.IsFullScreen
                && width > 0
                && height > 0
                && (_graphics.PreferredBackBufferWidth != width || _graphics.PreferredBackBufferHeight != height))
            {
                _graphics.PreferredBackBufferWidth = width;
                _graphics.PreferredBackBufferHeight = height;
                _graphics.ApplyChanges();
            }

            _renderer.Draw(_scenes.View, _scheduler.InterpolationAlpha);

            _drawOverlay?.Invoke(_renderer);
            _renderer.Submit();

            GraphicsDevice.Present();
        }
        finally
        {
            _deviceHeld = false;
        }
    }

    // Only a change the run asks for reaches the window. A window the player resized keeps the
    // player's size until the run asks for another.
    private void ApplyDisplay(Display display)
    {
        WindowRequest requested = new(display.Fullscreen, display.WindowSize, display.VSync);
        if (requested == _applied)
        {
            return;
        }

        // Held across the change, which raises the window events the resize watch answers. A redraw
        // landing inside it would reach a half-applied device.
        _deviceHeld = true;

        try
        {
            (int Width, int Height) windowed = (_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
            if (requested.WindowSize != _applied.WindowSize)
            {
                (int displayWidth, int displayHeight, float displayScale) = _builder.Platform.WindowDisplay(new WindowHandle(Window.Handle));
                windowed = display.WindowPixels(displayWidth, displayHeight, displayScale);
            }

            // A reset gives the back buffer the preferred extent, and only a window that changes size
            // raises the event that sizes it back to the window. A window that stays fullscreen raises
            // none. It keeps its back buffer through the reset and takes the windowed extent after.
            bool staysFullscreen = requested.Fullscreen && _graphics.IsFullScreen;
            PresentationParameters shown = GraphicsDevice.PresentationParameters;
            (_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight) =
                staysFullscreen ? (shown.BackBufferWidth, shown.BackBufferHeight) : windowed;
            _graphics.IsFullScreen = requested.Fullscreen;
            _graphics.SynchronizeWithVerticalRetrace = requested.VSync && !_builder.Uncapped;
            _graphics.ApplyChanges();

            if (staysFullscreen)
            {
                (_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight) = windowed;
            }
        }
        finally
        {
            _deviceHeld = false;
        }

        _applied = requested;
    }

    private readonly record struct WindowRequest(bool Fullscreen, System.Numerics.Vector2 WindowSize, bool VSync);
}
