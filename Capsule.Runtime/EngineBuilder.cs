using Capsule.Input;
using Capsule.Runtime.Input;

namespace Capsule.Runtime;

/// <summary>
/// Fluent configuration for one engine host. Every <c>With</c> validates eagerly, so a
/// misconfiguration throws at the call site that caused it rather than inside the loop, and
/// returns the builder it was called on, so a chain keeps whatever the entry point handed it.
/// <see cref="Run"/> blocks until the game exits.
/// </summary>
/// <typeparam name="TBuilder">The concrete builder, which every <c>With</c> returns.</typeparam>
public abstract class EngineBuilder<TBuilder>
    where TBuilder : EngineBuilder<TBuilder>
{
    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 720;
    private const int DefaultStepHertz = 60;
    private const double DefaultSpikeClampSeconds = 0.25;

    private readonly ActionBindings _bindings = new();

    private string _windowTitle;
    private int _windowWidth = DefaultWindowWidth;
    private int _windowHeight = DefaultWindowHeight;
    private bool _resizable = true;
    private bool _fullscreen;
    private (int Width, int Height)? _renderResolution;
    private double _stepSeconds = 1.0 / DefaultStepHertz;
    private double _maxFrameSeconds = DefaultSpikeClampSeconds;
    private float _stickDeadzone = PadFilter.DefaultStickDeadzone;
    private float _triggerDeadzone = PadFilter.DefaultTriggerDeadzone;
    private string? _crashLogAppName;

    /// <param name="gameName">
    /// The game's display name: the window's title, and the crash log's folder as a slug of it.
    /// </param>
    /// <exception cref="ArgumentException">The name is blank, or no safe directory name slugs out of it.</exception>
    private protected EngineBuilder(string gameName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        _windowTitle = gameName;
        _crashLogAppName = SafeName.Slug(gameName)
            ?? throw new ArgumentException(
                $"A game name must slug to one safe directory name for its crash log, and '{gameName}' does not: "
                + "it holds no letter or digit, or what remains is a reserved device name.",
                nameof(gameName));
    }

    private TBuilder Self => (TBuilder)this;

    /// <summary>The window's title, which is the game's name unless this replaces it.</summary>
    public TBuilder WithWindowTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        _windowTitle = title;

        return Self;
    }

    /// <summary>
    /// The windowed-mode window: opened at this size unless the game boots fullscreen, and
    /// returned to at this size whenever fullscreen is left. Defaults to 1280x720, resizable.
    /// </summary>
    /// <param name="width">Client width in pixels.</param>
    /// <param name="height">Client height in pixels.</param>
    /// <param name="resizable">Whether the player may drag the window's edges; windowed mode only.</param>
    public TBuilder WithWindow(int width, int height, bool resizable = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _windowWidth = width;
        _windowHeight = height;
        _resizable = resizable;

        return Self;
    }

    /// <summary>
    /// Boots fullscreen — borderless, at the desktop's own resolution. Alt+Enter toggles
    /// either way from there.
    /// </summary>
    public TBuilder WithFullscreen()
    {
        _fullscreen = true;

        return Self;
    }

    /// <summary>
    /// Rasterises the world into a fixed-size surface and letterboxes that into the window,
    /// so the window's size stops changing what a frame contains. Left unset, the world
    /// rasterises straight into the window at its live size, with no resolution ceiling.
    /// <para>
    /// These are pixels; a camera's <c>ViewportSize</c> is world units. The two are independent,
    /// and coincide only where a game wants one world unit to be one pixel.
    /// </para>
    /// </summary>
    /// <param name="width">Render-target width in pixels.</param>
    /// <param name="height">Render-target height in pixels.</param>
    public TBuilder WithRenderResolution(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _renderResolution = (width, height);

        return Self;
    }

    /// <param name="hertz">Simulation steps per second of simulated time.</param>
    public TBuilder WithFixedStep(int hertz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);

        _stepSeconds = 1.0 / hertz;

        return Self;
    }

    /// <summary>
    /// Ceiling on the simulated time one frame may contribute. Without it a long stall
    /// (breakpoint, window drag) queues more steps than the following frames can run, and
    /// the accumulator never drains.
    /// </summary>
    /// <param name="seconds">
    /// Real seconds, positive and finite, never below one fixed step; a shorter ceiling
    /// could not carry a whole step, and <see cref="Run"/> — where both values are known —
    /// rejects it.
    /// </param>
    public TBuilder WithSpikeClamp(double seconds)
    {
        // NaN passes every comparison-based guard and an infinite ceiling never binds:
        // either would silently disable the clamp the method exists to set.
        if (!double.IsFinite(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A spike clamp must be a finite number of seconds.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);

        _maxFrameSeconds = seconds;

        return Self;
    }

    /// <summary>
    /// A stick reading inside <paramref name="stick"/> radially reads centred and a trigger
    /// pull below <paramref name="trigger"/> reads released; past either, what remains is
    /// remapped onto [0, 1], so full deflection stays reachable.
    /// </summary>
    /// <param name="stick">Stick radius, in [0, 1); 0 applies no stick deadzone.</param>
    /// <param name="trigger">Trigger pull, in [0, 1); 0 applies no trigger deadzone.</param>
    public TBuilder WithGamepadDeadzones(float stick, float trigger)
    {
        RequireDeadzone(stick, nameof(stick));
        RequireDeadzone(trigger, nameof(trigger));

        _stickDeadzone = stick;
        _triggerDeadzone = trigger;

        return Self;
    }

    /// <summary>
    /// Writes an escaping exception to <c>crash.log</c> under the OS-local application data
    /// folder for <paramref name="appName"/>, replacing the folder slugged from the game's name.
    /// </summary>
    /// <param name="appName">Used verbatim as one directory name, so it must be exactly that.</param>
    public TBuilder WithCrashLog(string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        if (!SafeName.IsOneSafeDirectoryName(appName))
        {
            throw new ArgumentException(
                "A crash-log application name must be a single directory name: no separators, no relative segment, no reserved device name, and no trailing dot or space.",
                nameof(appName));
        }

        _crashLogAppName = appName;

        return Self;
    }

    /// <summary>
    /// Lets an escaping exception go unrecorded. A windowed build has no console to print it to,
    /// so this trades the one artefact such a crash leaves behind for writing nothing to disk.
    /// </summary>
    public TBuilder WithoutCrashLog()
    {
        _crashLogAppName = null;

        return Self;
    }

    /// <summary>Registers action bindings; call it more than once and the registrations accumulate.</summary>
    public TBuilder WithBindings(Action<ActionBindings> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_bindings);

        return Self;
    }

    /// <summary>Opens the window and runs <paramref name="simulation"/> until it requests exit.</summary>
    public void Run(ISimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        // Neither call can see the other's value, so the pair settles here.
        if (_maxFrameSeconds < _stepSeconds)
        {
            throw new InvalidOperationException(
                $"A spike clamp of {_maxFrameSeconds} s is below the fixed step of {_stepSeconds} s: no frame could contribute a whole step, so the simulation would fall behind real time at any frame rate.");
        }

        EngineOptions options = new(
            _windowTitle,
            _windowWidth,
            _windowHeight,
            _resizable,
            _fullscreen,
            _renderResolution,
            _stepSeconds,
            _maxFrameSeconds,
            _stickDeadzone,
            _triggerDeadzone,
            _bindings);

        if (_crashLogAppName is null)
        {
            Host(options, simulation);
            return;
        }

        try
        {
            Host(options, simulation);
        }
        catch (Exception exception)
        {
            // A windowed build has no console, so an escaping exception would otherwise
            // vanish. Rethrow to preserve the exit code and the debugger break.
            CrashLog.TryWrite(_crashLogAppName, exception);
            throw;
        }
    }

    private static void RequireDeadzone(float value, string parameterName)
    {
        // NaN compares false to everything, so the range guards below cannot reject it.
        if (float.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A deadzone radius cannot be NaN.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, 1f, parameterName);
    }

    private static void Host(EngineOptions options, ISimulation simulation)
    {
        using CapsuleGame game = new(options, simulation);
        game.Run();
    }
}
