using System.Buffers;
using Capsule.Input;
using Capsule.Maps;
using Capsule.Runtime.Input;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

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

    // Where the map build hook lands its output in a shell's content, and the extension it
    // writes; RunScene resolves a map name against exactly that.
    private const string MapDirectory = "Assets/Maps";
    private const string MapExtension = ".map.json";

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
    private bool _fullscreen;
    private (int Width, int Height)? _renderResolution;
    private double _stepSeconds = 1.0 / DefaultStepHertz;
    private double _maxFrameSeconds = DefaultSpikeClampSeconds;
    private float _stickDeadzone = PadFilter.DefaultStickDeadzone;
    private float _triggerDeadzone = PadFilter.DefaultTriggerDeadzone;
    private string? _crashLogAppName;
    private SceneRegistry? _scenes;

    internal EngineBuilder()
    {
    }

    /// <summary>
    /// The windowed-mode window: opened at this size unless the game boots fullscreen, and
    /// returned to at this size whenever fullscreen is left.
    /// </summary>
    /// <param name="width">Client width in pixels.</param>
    /// <param name="height">Client height in pixels.</param>
    /// <param name="resizable">Whether the player may drag the window's edges; windowed mode only.</param>
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

    /// <summary>
    /// Boots fullscreen — borderless, at the desktop's own resolution. Alt+Enter toggles
    /// either way from there.
    /// </summary>
    public EngineBuilder WithFullscreen()
    {
        _fullscreen = true;

        return this;
    }

    /// <summary>
    /// Rasterises the world into a fixed-size surface and letterboxes that into the window,
    /// so the window's size stops changing what a frame contains. Left unset, the world
    /// rasterises straight into the window at its live size, with no resolution ceiling.
    /// <para>
    /// These are pixels; a camera's <c>Size</c> is world units. The two are independent, and
    /// coincide only where a game wants one world unit to be one pixel.
    /// </para>
    /// </summary>
    /// <param name="width">Render-target width in pixels.</param>
    /// <param name="height">Render-target height in pixels.</param>
    public EngineBuilder WithRenderResolution(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _renderResolution = (width, height);

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

    /// <summary>
    /// The scenes the game declares, as the registry generated into its logic assembly; the
    /// generated entry point passes it, and a hand-built <see cref="SceneRegistry"/> is the other
    /// way in. Every <see cref="RunScene{TScene}"/> resolves through it.
    /// </summary>
    public EngineBuilder WithScenes(SceneRegistry scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        _scenes = scenes;

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

    /// <summary>
    /// Opens the window and runs <typeparamref name="TScene"/> until game code requests exit. A
    /// scene composed from a map loads it first; one that is not runs as it is.
    /// </summary>
    /// <typeparam name="TScene">A scene the registry passed to <see cref="WithScenes"/> holds.</typeparam>
    /// <exception cref="InvalidOperationException">No scenes are configured, or none is that class.</exception>
    /// <exception cref="MapFormatException">The map file is malformed.</exception>
    /// <exception cref="SpawnException">A map object's spawn type is claimed by no entity.</exception>
    public void RunScene<TScene>()
        where TScene : Scene
        => RunScene(SceneTarget.ForScene(typeof(TScene)));

    /// <summary>
    /// Opens the window and runs a map until game code requests exit: as the class claiming that
    /// map name, or as a plain <see cref="Capsule.Scenes.MapScene"/> when no class claims it. The
    /// map is read from <c>Assets/Maps/{mapName}.map.json</c> beside the executable, where the map
    /// build hook ships it.
    /// </summary>
    /// <param name="mapName">A map's bare name, as its authoring source is named.</param>
    /// <exception cref="InvalidOperationException">No scenes are configured.</exception>
    /// <exception cref="MapFormatException">The map file is malformed.</exception>
    /// <exception cref="SpawnException">A map object's spawn type is claimed by no entity.</exception>
    public void RunScene(string mapName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);

        RunScene(SceneTarget.ForMap(mapName));
    }

    private void RunScene(in SceneTarget initialTarget)
    {
        SceneRegistry scenes = ConfiguredScenes();

        Scene Resolve(in SceneTarget target) => target.Kind switch
        {
            SceneTargetKind.Scene => ComposeScene(scenes, target.SceneType!),
            SceneTargetKind.Map => ComposeMap(scenes, target.MapName!),
            _ => throw new InvalidOperationException($"Unknown scene target kind '{target.Kind}'."),
        };

        using SceneHost host = new(initialTarget, Resolve);
        Run(host);
    }

    private SceneRegistry ConfiguredScenes() =>
        _scenes ?? throw new InvalidOperationException(
            "Running a scene needs the game's scene registry. Boot through "
            + "Capsule.Runtime.Generated.GameBoot.Configure(), generated into the shell already carrying it. "
            + "CapsuleEngine.Configure() is the unwired entry point and takes the registry through "
            + "WithScenes(GameScenes.Registry); reaching this from GameBoot instead means the shell "
            + "references no assembly declaring scenes.");

    private static Scene ComposeMap(SceneRegistry scenes, string mapName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, MapDirectory, MapFileName(mapName));
        Map map = MapFile.Load(path);

        try
        {
            return scenes.CreateForMap(mapName, map);
        }
        catch (SpawnException exception)
        {
            // The scene layer is pure and knows no paths; naming the map is this layer's job.
            throw new SpawnException($"{path}: {exception.Message}", exception);
        }
    }

    private static Scene ComposeScene(SceneRegistry scenes, Type sceneType) =>
        scenes.MapNameOf(sceneType) is { } mapName
            ? ComposeMap(scenes, mapName)
            : scenes.Create(sceneType);

    // Maps ship into one flat directory, so a name that is a path would either escape it or point
    // at a file the hook never wrote, and one Windows resolves as a device would not be a file at
    // all. A map-backed scene's name comes from its class, so this guards both boot verbs.
    private static string MapFileName(string mapName)
    {
        if (!string.Equals(Path.GetFileName(mapName), mapName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A map name is the bare name of its authoring source, not a path: '{mapName}'.",
                nameof(mapName));
        }

        if (!IsOneSafeDirectoryName(mapName))
        {
            throw new ArgumentException(
                $"A map name must be a single safe file name: no separators, no reserved device name, and no trailing dot or space: '{mapName}'.",
                nameof(mapName));
        }

        return mapName + MapExtension;
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
