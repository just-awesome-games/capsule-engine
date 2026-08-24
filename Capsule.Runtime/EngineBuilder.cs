using System.Buffers;
using Capsule.Input;
using Capsule.Runtime.Input;

namespace Capsule.Runtime;

/// <summary>
/// Fluent configuration for one engine host. Every <c>With</c> validates eagerly, so
/// a misconfiguration throws at the call site that caused it rather than inside the
/// loop. <see cref="Run"/> blocks until the game exits.
/// </summary>
public sealed class EngineBuilder
{
    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 720;
    private const int DefaultStepHertz = 60;
    private const double DefaultSpikeClampSeconds = 0.25;

    // Fixed rather than Path.GetInvalidFileNameChars(): the POSIX set rejects only '\0'
    // and '/', so a name accepted on a Linux build machine would fail on a player's
    // Windows box. The safe-name contract must not depend on where the game was built.
    private static readonly SearchValues<char> UnsafeNameChars = SearchValues.Create(UnsafeNameCharSet());

    // Windows resolves these as devices from any directory, matching on the stem before
    // the first dot, so "CON" and "CON.log" both fail rather than creating a directory.
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    private readonly ActionBindings _bindings = new();

    private string _windowTitle = "Capsule";
    private int _windowWidth = DefaultWindowWidth;
    private int _windowHeight = DefaultWindowHeight;
    private bool _resizable;
    private double _stepSeconds = 1.0 / DefaultStepHertz;
    private double _maxFrameSeconds = DefaultSpikeClampSeconds;
    private float _stickDeadzone = PadFilter.DefaultStickDeadzone;
    private float _triggerDeadzone = PadFilter.DefaultTriggerDeadzone;
    private string? _crashLogAppName;

    internal EngineBuilder()
    {
    }

    /// <param name="width">Back-buffer width in pixels.</param>
    /// <param name="height">Back-buffer height in pixels.</param>
    public EngineBuilder WithWindow(string title, int width, int height, bool resizable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _windowTitle = title;
        _windowWidth = width;
        _windowHeight = height;
        _resizable = resizable;

        return this;
    }

    /// <param name="hertz">Simulation steps per second of simulated time.</param>
    public EngineBuilder WithFixedStep(int hertz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);

        _stepSeconds = 1.0 / hertz;

        return this;
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
    public EngineBuilder WithSpikeClamp(double seconds)
    {
        // NaN passes every comparison-based guard and an infinite ceiling never binds:
        // either would silently disable the clamp the method exists to set.
        if (!double.IsFinite(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A spike clamp must be a finite number of seconds.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);

        _maxFrameSeconds = seconds;

        return this;
    }

    /// <summary>
    /// A stick reading inside <paramref name="stick"/> radially reads centred and a trigger
    /// pull below <paramref name="trigger"/> reads released; past either, what remains is
    /// remapped onto [0, 1], so full deflection stays reachable.
    /// </summary>
    /// <param name="stick">Stick radius, in [0, 1); 0 applies no stick deadzone.</param>
    /// <param name="trigger">Trigger pull, in [0, 1); 0 applies no trigger deadzone.</param>
    public EngineBuilder WithGamepadDeadzones(float stick, float trigger)
    {
        RequireDeadzone(stick, nameof(stick));
        RequireDeadzone(trigger, nameof(trigger));

        _stickDeadzone = stick;
        _triggerDeadzone = trigger;

        return this;
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

    /// <summary>
    /// Writes an escaping exception to <c>crash.log</c> under the OS-local application
    /// data folder for <paramref name="appName"/>, then rethrows it.
    /// </summary>
    /// <param name="appName">Used verbatim as one directory name, so it must be exactly that.</param>
    public EngineBuilder WithCrashLog(string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        if (!IsOneSafeDirectoryName(appName))
        {
            throw new ArgumentException(
                "A crash-log application name must be a single directory name: no separators, no relative segment, no reserved device name, and no trailing dot or space.",
                nameof(appName));
        }

        _crashLogAppName = appName;

        return this;
    }

    /// <summary>Registers action bindings; call it more than once and the registrations accumulate.</summary>
    public EngineBuilder WithBindings(Action<ActionBindings> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_bindings);

        return this;
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

    private static char[] UnsafeNameCharSet()
    {
        const string Reserved = "<>:\"/\\|?*";
        const int ControlCharCount = 0x20;

        char[] unsafeChars = new char[Reserved.Length + ControlCharCount];
        Reserved.CopyTo(unsafeChars);
        for (int control = 0; control < ControlCharCount; control++)
        {
            unsafeChars[Reserved.Length + control] = (char)control;
        }

        return unsafeChars;
    }

    private static bool IsOneSafeDirectoryName(string name)
    {
        if (name.AsSpan().IndexOfAny(UnsafeNameChars) >= 0)
        {
            return false;
        }

        // Catches "." and ".." with it: Windows trims trailing dots and spaces, so such a
        // name silently resolves to a different directory than the one it reads as.
        if (name[^1] is '.' or ' ')
        {
            return false;
        }

        ReadOnlySpan<char> stem = name.AsSpan();
        int dot = stem.IndexOf('.');
        if (dot >= 0)
        {
            stem = stem[..dot];
        }

        foreach (string reserved in ReservedDeviceNames)
        {
            if (stem.Equals(reserved, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static void Host(EngineOptions options, ISimulation simulation)
    {
        using CapsuleGame game = new(options, simulation);
        game.Run();
    }
}
