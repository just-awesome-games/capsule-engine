using Capsule.Input;
using Capsule.Runtime.Input;
using Capsule.Runtime.Rendering;
using Microsoft.Xna.Framework;

namespace Capsule.Runtime;

// The MonoGame host. Owns the window, the device and the clock, and drives the simulation on its
// own fixed-step accumulator rather than MonoGame's, so a run reproduces frame for frame.
internal sealed class CapsuleGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly EngineOptions _options;
    private readonly ISimulation _simulation;
    private readonly PadFilter _padFilter;
    private readonly FixedStepScheduler _scheduler;

    // Null unless the builder opted in, and owned by it: every use on the frame path is that
    // null check.
    private readonly FrameDiagnostics? _diagnostics;

    private TextureStore _textures = null!;
    private FrameRenderer _renderer = null!;
    private bool _windowRaised;
    private bool _fullscreenChordHeld;
    private bool _fullscreenChordQuarantined;

    internal CapsuleGame(EngineOptions options, ISimulation simulation, FrameDiagnostics? diagnostics)
    {
        _options = options;
        _diagnostics = diagnostics;
        _simulation = simulation;
        _padFilter = new PadFilter(options.StickDeadzone, options.TriggerDeadzone);
        _scheduler = new FixedStepScheduler(options.StepSeconds, options.MaxStepsPerFrame, options.Bindings);

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
        // Every registered texture, once, before the first frame: nothing is fetched while the
        // game is running.
        _textures = new TextureStore(GraphicsDevice, _options.Textures);
        _diagnostics?.Mark(FrameDiagnostics.Stage.TexturesResident);
        _renderer = new FrameRenderer(GraphicsDevice, _options.RenderResolution, _textures);

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

        if (_scheduler.Advance(gameTime.ElapsedGameTime.TotalSeconds, sampled, _simulation))
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
            // Null when construction failed before LoadContent ran.
            _renderer?.Dispose();
            _textures?.Dispose();
        }

        base.Dispose(disposing);
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
            _graphics.IsFullScreen = !_graphics.IsFullScreen;
            _graphics.ApplyChanges();
        }

        _fullscreenChordHeld = held;

        // The quarantine outlives the chord, ending only once both keys are up: releasing one
        // first would otherwise hand the other to the simulation as a fresh press.
        _fullscreenChordQuarantined = held || (_fullscreenChordQuarantined && (alt || enter));

        return _fullscreenChordQuarantined;
    }
}
