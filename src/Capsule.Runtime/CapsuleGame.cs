using Capsule.Input;
using Capsule.Runtime.Input;
using Capsule.Runtime.Rendering;
using Microsoft.Xna.Framework;

namespace Capsule.Runtime;

/// <summary>
/// The MonoGame host. Owns the window, the device and the clock, and drives the
/// simulation on its own fixed-step accumulator rather than MonoGame's, so a
/// harness can reproduce a run frame for frame.
/// </summary>
internal sealed class CapsuleGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly EngineOptions _options;
    private readonly ISimulation _simulation;
    private readonly PadFilter _padFilter;
    private readonly FixedStepScheduler _scheduler;

    private FrameRenderer _renderer = null!;
    private bool _fullscreenChordHeld;
    private bool _fullscreenChordQuarantined;

    internal CapsuleGame(EngineOptions options, ISimulation simulation)
    {
        _options = options;
        _simulation = simulation;
        _padFilter = new PadFilter(options.StickDeadzone, options.TriggerDeadzone);
        _scheduler = new FixedStepScheduler(options.StepSeconds, options.MaxFrameSeconds, options.Bindings);

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = options.WindowWidth,
            PreferredBackBufferHeight = options.WindowHeight,
            // Borderless. The preferred size is ignored while fullscreen, so leaving it
            // restores the configured window with no work here.
            HardwareModeSwitch = false,
            IsFullScreen = options.Fullscreen,
        };

        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.Title = options.WindowTitle;
        Window.AllowUserResizing = options.Resizable;
    }

    protected override void LoadContent()
    {
        _renderer = new FrameRenderer(GraphicsDevice, _options.RenderResolution);

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        // Sampled every frame including one that drains no step: the latch is what
        // carries that frame's input to the step that eventually runs. Both devices
        // land in one snapshot; they occupy disjoint parts of it.
        DeviceSnapshot sampled = GamepadSampler.SampleOnto(KeyboardSampler.Sample(), _padFilter);

        // Alt+Enter is the host's, never a bindable action. Withheld from the snapshot for
        // the whole gesture, or a game that binds Enter reads a press out of it.
        if (ConsumeFullscreenChord(sampled))
        {
            sampled = sampled.Without(Key.Enter).Without(Key.LeftAlt).Without(Key.RightAlt);
        }

        if (_scheduler.Advance(gameTime.ElapsedGameTime.TotalSeconds, sampled, _simulation))
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // alpha is in [0, 1) because Update drains the accumulator below one step.
        _renderer.Draw(_simulation.View, _scheduler.InterpolationAlpha);

        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Null when construction failed before LoadContent ran.
            _renderer?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Toggles the window on the chord's leading edge; returns whether Alt and Enter are
    /// still quarantined from the simulation.
    /// </summary>
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

        // The quarantine outlives the chord, ending only once both keys are up: releasing
        // one first would otherwise hand the other to the simulation as a fresh press.
        _fullscreenChordQuarantined = held || (_fullscreenChordQuarantined && (alt || enter));

        return _fullscreenChordQuarantined;
    }
}
