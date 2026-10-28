using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Audio;
using Capsule.Runtime.DevTools.Latency;
using Capsule.Runtime.DevTools.Overlay;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Runtime.Gpu;
using Capsule.Runtime.Input;
using Capsule.Runtime.Platform;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;

namespace Capsule.Runtime;

// The windowed host, run as SDL's main callbacks. SDL calls Frame once a frame, from its own loop or
// from inside the operating system's modal move or resize, after handing over the events pumped since
// the last one. The host owns the window, the device and the clock, and drives the scenes on its own
// fixed-step accumulator, which makes a run reproduce frame for frame.
internal sealed unsafe class WindowHost
{
    // The host SDL's callbacks reach, for the length of Run. One windowed run at a time.
    private static WindowHost? Running;

    private readonly EngineBuilder _builder;
    private readonly SceneHost _scenes;
    private readonly MouseSampler _mouse = new();
    private readonly FixedStepScheduler _scheduler;
    private readonly GamepadRumble _rumble = new(GamepadRumble.WriteToPad);
    private readonly FramePacer _pacer = new();
    private readonly InputFeed _input = new();

    // What the player's keyboard layout prints on each key, indexed by key, which the run's input
    // state reads key characters from. Read at open and rewritten in place on a layout change.
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

    // The logical name a shell embeds its window icon under.
    private const string WindowIconResource = "Icon.png";

    // The real time a fast-forwarding replay steps for in one frame, which keeps the window answering.
    private static readonly long FastForwardTicks = Stopwatch.Frequency / 30;

    // Opens the overlay over a replay that has reached its end, with the outcome on its status line. Null
    // in a shipping build.
    private readonly Action<string>? _holdOverlay;

    // SDL's window, zero until Open creates it and after Close destroys it.
    private nint _window;

    // Null until Open. Close runs without them after a failed open.
    private GpuDevice _gpu = null!;
    private TextureStore _textures = null!;
    private ShaderStore _shaders = null!;
    private FrameRenderer _renderer = null!;
    private CursorApplier _cursor = null!;
    private GamepadSampler _pad = null!;

    // All null when the platform opened no audio output, which makes every command and preload a
    // no-op instead of failing the run. The device owns the store and the player.
    private SoundDevice? _device;
    private SoundStore? _sounds;
    private AudioPlayer? _audio;

    // The first exception a callback caught, which Run throws once SDL has returned.
    private ExceptionDispatchInfo? _failure;

    private bool _closed;
    private bool _windowRaised;
    private bool _focusSeen;
    private bool _deviceSeeded;
    private bool _minimized;

    // A windowed replay is announced once at its end, and the refresh rate is read again once it hands
    // over to live input.
    private bool _replaying;
    private bool _replayAnnounced;

    // When the current frame started, from which the next frame's elapsed time is measured.
    private long _frameStarted;

    // The run's display as the window last applied it.
    private WindowRequest _applied;

    // The mode the window presents in, which the GPU may have lowered from the one asked for.
    private VSyncMode _presented;

    // The seconds the frame cap holds this frame from the next, 0 for no wait.
    private double _capPeriodSeconds;

    // The display scale per pixel density the window's size was last set for. Above 1 only where a
    // window coordinate is a pixel, which a move between displays leaves unscaled.
    private float _sizedScale = 1f;

    // recorder records the run and replay replays one into it. Both are null in a shipping build.
    internal WindowHost(EngineBuilder builder, SceneHost scenes, FrameDiagnostics? diagnostics, Recorder? recorder = null, Replay? replay = null)
    {
        _builder = builder;
        _diagnostics = diagnostics;
        _scenes = scenes;
        _scheduler = new FixedStepScheduler(
            builder.StepSeconds,
            builder.MaxStepsPerFrame,
            builder.Input.Bindings,
            replay is null ? builder.Driver : null,
            scenes,
            recorder: recorder,
            replay: replay);
        _replaying = replay is not null;

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
            DebugOverlay overlay = new(builder.Input.DebugOverlayButton, _scheduler, scenes, builder.Scenes, recordings: recorder is null ? null : builder.RecordingsDirectory());
            _overlayHost = overlay;
            _holdOverlay = overlay.HoldAt;
            _interceptOverlay = (snapshot, renderer) => overlay.Intercept(
                snapshot,
                renderer.ScreenLayer,
                DebugOverlay.ScaleFor(renderer.WindowSize.Height),
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

        _diagnostics?.Mark(FrameDiagnostics.Stage.HostConstructed);
    }

    internal long SimulationTick => _scheduler.Tick;

    // Runs the scenes until they request exit or the window closes. An exception escaping a callback
    // ends the run and is rethrown here, once the host has closed and SDL has quit.
    internal void Run()
    {
        Running = this;

        try
        {
            Sdl3.SDL_SetAppMetadata(_builder.GameName, null, null);
            Sdl3.SDL_EnterAppMainCallbacks(0, null, &OnInit, &OnIterate, &OnEvent, &OnQuit);
        }
        finally
        {
            Running = null;
            Close();
        }

        _failure?.Throw();
    }

    // An exception unwinding into SDL's frames terminates the process. Each callback ends the run with
    // it instead.
    [UnmanagedCallersOnly]
    private static Sdl3.AppResult OnInit(nint* appState, int argc, byte** argv)
    {
        WindowHost host = Running!;

        try
        {
            host.Open();
            return Sdl3.AppResult.Continue;
        }
        catch (Exception exception)
        {
            return host.Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static Sdl3.AppResult OnIterate(nint appState)
    {
        WindowHost host = Running!;

        try
        {
            return host.Frame() ? Sdl3.AppResult.Continue : Sdl3.AppResult.Success;
        }
        catch (Exception exception)
        {
            return host.Fail(exception);
        }
    }

    // Every event reaches the input feed, on this thread, between frames. Closing the window quits.
    [UnmanagedCallersOnly]
    private static Sdl3.AppResult OnEvent(nint appState, nint sdlEvent)
    {
        WindowHost host = Running!;

        try
        {
            if (*(uint*)sdlEvent == Sdl3.EventQuit)
            {
                return Sdl3.AppResult.Success;
            }

            host._input.Record(sdlEvent);
            return Sdl3.AppResult.Continue;
        }
        catch (Exception exception)
        {
            return host.Fail(exception);
        }
    }

    // SDL quits once this returns, after a failure as after an exit.
    [UnmanagedCallersOnly]
    private static void OnQuit(nint appState, Sdl3.AppResult result)
    {
        WindowHost host = Running!;

        try
        {
            host.Close();
        }
        catch (Exception exception)
        {
            host.Fail(exception);
        }
    }

    private Sdl3.AppResult Fail(Exception exception)
    {
        _failure ??= ExceptionDispatchInfo.Capture(exception);

        return Sdl3.AppResult.Failure;
    }

    // Opens the window and the device, and loads what the first scene needs. The window shows once
    // the device has claimed it, and its first frame raises it.
    private void Open()
    {
        if (!Sdl3.SDL_InitSubSystem(Sdl3.InitVideo))
        {
            throw new InvalidOperationException($"The window could not open: {Sdl3.Error()}.");
        }

        // The run has started by now, and the window opens as its start left the display.
        Display display = _scenes.Run.Display;
        OpenWindow(display);
        _gpu = new GpuDevice(_window, _builder.GpuDriver, _builder.FramesInFlight);
        if (Development.IsSupported)
        {
            Log.Info($"GPU: {_gpu.Description}");
        }

        ApplyVSync(display.VSync);
        _applied = new WindowRequest(display.Fullscreen, display.WindowSize, display.VSync);
        ReadRefreshRate();
        _diagnostics?.Mark(FrameDiagnostics.Stage.DeviceReady);

        TextureSamplers samplers = new(_gpu);
        _textures = new TextureStore(_gpu, samplers, _builder.Platform);
        _shaders = new ShaderStore(_gpu, _builder.Platform, _gpu.SwapchainFormat);

        if (SoundDevice.TryOpen(_builder.Platform, _builder.AudioLeadFrames) is { } device)
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
        _cursor = new CursorApplier(_textures, confined => Sdl3.SDL_SetWindowMouseGrab(_window, confined));

        // The pads already connected raise their added events too, which the sampler opens once.
        _input.UseTriggerDeadzone(_builder.Input.TriggerDeadzone);
        _pad = new GamepadSampler(_builder.Platform.OpenContent);

        // The layout gives key characters from the first step, and the feed reports each later change.
        KeyboardSampler.ReadLayout(_keyLayout);
        _scheduler.UseKeyLayout(_keyLayout);

        // The first frame samples the mouse before it draws. The mapping is settled here and the first
        // step reads a canvas position.
        _renderer.ResolveScreenLayer(scenes.View);

        Sdl3.SDL_ShowWindow(_window);
        _frameStarted = Stopwatch.GetTimestamp();
    }

    // Hidden, at the run's size on the display it opens on.
    private void OpenWindow(Display display)
    {
        ulong flags = Sdl3.WindowHidden | Sdl3.WindowHighPixelDensity;
        flags |= _builder.Resizable ? Sdl3.WindowResizable : 0;
        flags |= display.Fullscreen ? Sdl3.WindowFullscreen : 0;

        fixed (byte* title = Encoding.UTF8.GetBytes(_builder.WindowTitle + "\0"))
        {
            _window = Sdl3.SDL_CreateWindow(title, (int)display.WindowSize.X, (int)display.WindowSize.Y, flags);
        }

        if (_window == nint.Zero)
        {
            throw new InvalidOperationException($"The window could not open: {Sdl3.Error()}.");
        }

        SetWindowIcon();
        ResizeWindow(display);
    }

    // The shell's embedded icon, which every Capsule shell's build supplies.
    private void SetWindowIcon()
    {
        using Stream? resource = Assembly.GetEntryAssembly()?.GetManifestResourceStream(WindowIconResource);
        if (resource is null)
        {
            return;
        }

        byte[] png = new byte[resource.Length];
        resource.ReadExactly(png);
        fixed (byte* data = png)
        {
            Sdl3.Surface* icon = Sdl3.SDL_LoadPNG_IO(Sdl3.SDL_IOFromConstMem(data, (nuint)png.Length), closeSource: true);
            if (icon == null)
            {
                Log.Warning($"The window icon could not be read: {Sdl3.Error()}.");
                return;
            }

            Sdl3.SDL_SetWindowIcon(_window, (nint)icon);
            Sdl3.SDL_DestroySurface((nint)icon);
        }
    }

    // One frame: sample, step, draw, then wait for the next. False ends the run.
    private bool Frame()
    {
        // SDL pumped and handed over the events just before this, after the last frame's wait. The
        // frame starts at its input.
        long frameStarted = Stopwatch.GetTimestamp();
        double elapsedSeconds = (double)(frameStarted - _frameStarted) / Stopwatch.Frequency;
        _frameStarted = frameStarted;

        _diagnostics?.BeginUpdate();

        // Sampled every frame, including one that drains no step, and the latch carries that frame's
        // input to the step that eventually runs. The pointer is mapped through the screen layer's
        // placement. It reaches the simulation as a canvas position. SDL's focus flag reads false
        // until the operating system grants focus. An inactive window reads no device. The presses
        // since the last sample are taken either way, and an inactive window drops them.
        ulong flags = Sdl3.SDL_GetWindowFlags(_window);
        bool active = (flags & Sdl3.WindowInputFocus) != 0;
        _minimized = (flags & Sdl3.WindowMinimized) != 0;
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
        sampled = _mouse.SampleOnto(sampled, _renderer.ScreenLayer, Sdl3.SDL_GetWindowPixelDensity(_window), active, events);

        // A window that has never had focus has not lost it. A launch from a terminal raises no loss
        // before the first frame raises the window.
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

        // Before the steps, which may read it.
        if (events.RefreshRateMayHaveChanged)
        {
            ReadRefreshRate();
        }

        // A replay released at its end has handed over to live input, and the display's rate is the
        // window's again.
        if (_replaying && _scheduler.Replay is null)
        {
            _replaying = false;
            ReadRefreshRate();
        }

        // The run owns the pace and the scheduler holds what is applied. Copied after the overlay's
        // intercept, which may move it, and before the frame's elapsed time is spent. The output is
        // the window's extent in pixels, and a replay writes its recorded one into each step.
        _scheduler.TimeScale = _scenes.Run.TimeScale;
        (int outputWidth, int outputHeight) = _renderer.WindowSize;
        _scheduler.Output = new Vector2(outputWidth, outputHeight);

        // A replay fast-forwards, silent and undrawn, unless frame diagnostics time it at the run's pace.
        bool fastForward = _scheduler.Replay is { Finished: false } && _diagnostics is null;
        if (fastForward)
        {
            _audio?.Suspend();
        }

        bool exiting = fastForward
            ? _scheduler.FastForward(FastForwardTicks, _scenes)
            : _scheduler.Advance(elapsedSeconds, sampled, _scenes);

        // At its end a replay holds the run with the overlay open, and the log and the status line say
        // whether it matched.
        if (_scheduler.Replay is { Finished: true } ended && !_replayAnnounced)
        {
            _replayAnnounced = true;
            Log.Info(ended.Outcome);
            _holdOverlay?.Invoke(ended.Outcome);
        }

        // After the steps, which may have changed the trigger deadzone. The next frame's events are
        // read against the deadzone the run settled.
        _input.UseTriggerDeadzone(_builder.Input.TriggerDeadzone);

        _latencyStepped?.Invoke(_scheduler.StepsThisFrame);

        _updateOverlay?.Invoke(_renderer);

        // After the steps that may have asked for a prefetch. Texture pages upload in the frame's own
        // copy pass.
        _sounds?.Pump();

        // Every frame, including one that drained no step. The voices the mix thread has ended are
        // retired, and a fault on the mix or decode threads is logged.
        _device?.Update();

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

        // Every frame, after the steps. The window keeps its apparent size across displays, then follows
        // the settled display, and the next frame waits the period it asks for now.
        if (events.DisplayScaleMayHaveChanged)
        {
            KeepApparentSize(flags);
        }

        ApplyDisplay(_scenes.Run.Display);
        _capPeriodSeconds = FramePacer.PeriodSeconds(_scenes.Run.Display, active, _minimized, _builder.StepSeconds, _builder.Uncapped);
        _scheduler.FramePeriodSeconds = FramePacer.FrameSeconds(
            _capPeriodSeconds,
            !_minimized && _presented == VSyncMode.On,
            _scenes.Run.Display.RefreshRate);

        // A drawn frame ends a timed diagnostics run once it is submitted. A minimized one draws none.
        exiting |= _minimized && _diagnostics is { Expired: true };

        _diagnostics?.EndUpdate(_scheduler.StepsThisFrame, _scheduler.Tick);

        // A minimized window shows nothing. Its frames build and draw nothing. A run that is exiting
        // draws nothing either: its scenes have released what the frame would draw. Nor does a replay
        // fast-forwarding.
        if (!_minimized && !exiting && !fastForward)
        {
            exiting = Draw();
        }

        if (exiting)
        {
            // The motors are rested before the window goes, and Close repeats it harmlessly.
            _rumble.Silence();
            return false;
        }

        // SDL pumps the events once this returns. The next frame samples what the player did while
        // this one waited. The swapchain wait holds the sample back until the device can take the
        // frame it starts, and the cap's wait holds it to the run's rate.
        if (fastForward)
        {
            return true;
        }

        if (!_minimized)
        {
            _gpu.WaitForSwapchain();
        }

        _pacer.Wait(_capPeriodSeconds);
        return true;
    }

    // Draws and submits the frame, and returns whether a timed diagnostics run has spent its budget.
    private bool Draw()
    {
        // A launch from a terminal leaves the window behind the terminal and deaf to input until it
        // is raised.
        if (!_windowRaised)
        {
            _windowRaised = true;
            _builder.Platform.RaiseWindow(_window);
        }

        // The first read since the last step builds the view, timed apart from the submission.
        _diagnostics?.BeginView();
        FrameView view = _scenes.View;

        // Taken before the frame records, which draws a capture's window pass offscreen. A request
        // raised while the window is minimised, or while a stand-in camera frames the view, stands
        // until the game's own frame draws.
        string? capturePath = _renderer.CanCaptureFrame && _scenes.ViewCamera is null && _scenes.TryTakeFrameCapture(out string requested)
            ? requested
            : null;

        _diagnostics?.BeginDraw();

        // alpha is in [0, 1) because the scheduler drains the accumulator below one step.
        _renderer.Draw(view, _scheduler.InterpolationAlpha, capturePath);

        // The diagnostics cover the game frame's recording, a capture's download included. The
        // overlay and the submission, which presents and writes a capture, are excluded.
        bool budgetSpent = _diagnostics?.EndDraw() ?? false;

        _drawOverlay?.Invoke(_renderer);
        _renderer.Submit();
        _latencyPresented?.Invoke();

        return budgetSpent;
    }

    // Releases everything Open made, whichever step it reached. Runs once. Every step runs whatever an
    // earlier one threw, and the first failure is rethrown after the last.
    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Exception? failure = null;

        // First, and on every path out of the host: a crash closes the host before the crash log is
        // written, and a pad left buzzing is what the player would notice.
        Release(ref failure, _rumble.Silence);

        Release(ref failure, () => _overlayHost?.Dispose());
        Release(ref failure, () => _latency?.Dispose());
        Release(ref failure, () => _cursor?.Dispose());

        // After the motors rest, which writes to the open pads.
        Release(ref failure, () => _pad?.Dispose());

        // The scheduler and the scenes outlive this host. The scenes release their assets after it
        // closes, and the stores they would release into are disposed below.
        _scheduler.StepCompleted = null;
        _scenes.BoundaryReached = null;
        _scenes.PrepareAssets = null;
        _scenes.PrefetchAssets = null;
        _scenes.SceneEntered = null;

        // The mix thread stops before the output, the clips and the decoders it reads go.
        Release(ref failure, () => _device?.Dispose());

        Release(ref failure, () => _renderer?.Dispose());
        Release(ref failure, () => _shaders?.Dispose());
        Release(ref failure, () => _textures?.Dispose());
        Release(ref failure, () => _gpu?.Dispose());

        if (_window != nint.Zero)
        {
            Sdl3.SDL_DestroyWindow(_window);
            _window = nint.Zero;
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    private static void Release(ref Exception? failure, Action release)
    {
        try
        {
            release();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
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

    // Only a change the run asks for reaches the window. A window the player resized keeps the
    // player's size until the run asks for another.
    private void ApplyDisplay(Display display)
    {
        WindowRequest requested = new(display.Fullscreen, display.WindowSize, display.VSync);
        if (requested == _applied)
        {
            return;
        }

        // The size first, which a window entering fullscreen leaves it at.
        if (requested.WindowSize != _applied.WindowSize)
        {
            ResizeWindow(display);
        }

        if (requested.Fullscreen != _applied.Fullscreen)
        {
            Sdl3.SDL_SetWindowFullscreen(_window, requested.Fullscreen);
        }

        if (requested.VSync != _applied.VSync)
        {
            ApplyVSync(requested.VSync);
        }

        _applied = requested;
    }

    // Gives the window the run's size, scaled for the display it is on and fit inside it. A fullscreen
    // window takes it on leaving fullscreen.
    private void ResizeWindow(Display display)
    {
        (int displayWidth, int displayHeight) = DisplayPixels();
        float scale = Sdl3.SDL_GetWindowDisplayScale(_window);
        (int width, int height) = display.WindowPixels(displayWidth, displayHeight, scale > 0f ? scale : 1f);
        SetWindowPixels(width, height);
    }

    // A windowed window moved to a display of another scale keeps the size it appears at, the player's
    // own size included. Where a window coordinate is a pixel SDL keeps the pixels, which would show the
    // window at a fraction or a multiple of its size. A fullscreen or maximized window takes the
    // display's size and is rescaled once it is neither.
    private void KeepApparentSize(ulong flags)
    {
        if ((flags & (Sdl3.WindowFullscreen | Sdl3.WindowMaximized | Sdl3.WindowMinimized)) != 0)
        {
            return;
        }

        float scale = ApparentScale();
        if (scale == _sizedScale)
        {
            return;
        }

        int width;
        int height;
        if (!Sdl3.SDL_GetWindowSizeInPixels(_window, &width, &height))
        {
            return;
        }

        (int displayWidth, int displayHeight) = DisplayPixels();
        (width, height) = Display.Rescaled(width, height, _sizedScale, scale, displayWidth, displayHeight);
        SetWindowPixels(width, height);
    }

    // The desktop extent in pixels of the display the window is on, unbounded when SDL cannot say.
    private (int Width, int Height) DisplayPixels()
    {
        Sdl3.DisplayMode* mode = Sdl3.SDL_GetDesktopDisplayMode(Sdl3.SDL_GetDisplayForWindow(_window));

        return mode == null
            ? (int.MaxValue, int.MaxValue)
            : ((int)(mode->Width * mode->PixelDensity), (int)(mode->Height * mode->PixelDensity));
    }

    // The window's size in pixels, set in window coordinates, and the scale it was set for.
    private void SetWindowPixels(int width, int height)
    {
        float density = PixelDensity();
        Sdl3.SDL_SetWindowSize(_window, (int)MathF.Round(width / density), (int)MathF.Round(height / density));
        _sizedScale = ApparentScale();
    }

    // The display's scale that window coordinates do not already carry: 1 where SDL scales the window
    // with its display, and the display scale where a window coordinate is a pixel.
    private float ApparentScale()
    {
        float scale = Sdl3.SDL_GetWindowDisplayScale(_window);

        return (scale > 0f ? scale : 1f) / PixelDensity();
    }

    private float PixelDensity()
    {
        float density = Sdl3.SDL_GetWindowPixelDensity(_window);

        return density > 0f ? density : 1f;
    }

    // The development flag uncaps the window whatever the run asks for.
    private void ApplyVSync(VSyncMode requested)
    {
        VSyncMode asked = _builder.Uncapped ? VSyncMode.Off : requested;
        _presented = _gpu.ApplyVSync(asked);
        if (_presented != asked)
        {
            Log.Info($"The GPU cannot present with VSync {asked}, and the window presents with {_presented}.");
        }
    }

    // The desktop mode's, which a borderless fullscreen window keeps: the exact ratio where SDL has one,
    // its rounded rate where it has not, and 0 where it cannot say.
    private void ReadRefreshRate()
    {
        Sdl3.DisplayMode* mode = Sdl3.SDL_GetDesktopDisplayMode(Sdl3.SDL_GetDisplayForWindow(_window));
        _scenes.Run.Display.RefreshRate = mode == null ? 0
            : mode->RefreshRateDenominator > 0 ? (double)mode->RefreshRateNumerator / mode->RefreshRateDenominator
            : mode->RefreshRate;
    }

    private readonly record struct WindowRequest(bool Fullscreen, Vector2 WindowSize, VSyncMode VSync);
}
