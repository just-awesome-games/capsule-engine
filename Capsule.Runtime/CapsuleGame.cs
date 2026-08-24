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
    private readonly InputState _input;
    private readonly PadFilter _padFilter;
    private readonly SnapshotLatch _latch = new();

    private FrameRenderer _renderer = null!;
    private double _accumulator;
    private long _tick;

    internal CapsuleGame(EngineOptions options, ISimulation simulation)
    {
        _options = options;
        _simulation = simulation;
        _input = new InputState(options.Bindings);
        _padFilter = new PadFilter(options.StickDeadzone, options.TriggerDeadzone);

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = options.WindowWidth,
            PreferredBackBufferHeight = options.WindowHeight,
        };

        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.Title = options.WindowTitle;
        Window.AllowUserResizing = options.Resizable;
    }

    protected override void LoadContent()
    {
        _renderer = new FrameRenderer(GraphicsDevice);

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        // Sampled every frame including one that drains no step: the latch is what
        // carries that frame's input to the step that eventually runs. Both devices
        // land in one snapshot; they occupy disjoint parts of it.
        _latch.Observe(GamepadSampler.SampleOnto(KeyboardSampler.Sample(), _padFilter));

        _accumulator += Math.Min(gameTime.ElapsedGameTime.TotalSeconds, _options.MaxFrameSeconds);
        while (_accumulator >= _options.StepSeconds)
        {
            _input.Advance(_latch.ConsumeStepSnapshot());
            _simulation.Step(new StepContext(_options.StepSeconds, _input, _tick));
            _accumulator -= _options.StepSeconds;
            _tick++;

            if (_simulation.ExitRequested)
            {
                Exit();
                break;
            }
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // alpha is in [0, 1) because Update drains the accumulator below one step.
        _renderer.Draw(_simulation.View, (float)(_accumulator / _options.StepSeconds));

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
}
