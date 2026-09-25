using System.Numerics;
using Capsule.Audio;
using Capsule.Input;
using Capsule.Persistence;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule;

/// <summary>
/// One launch of the game from boot to exit, as the simulation sees it. The engine installs the run on
/// every scene before the scene starts, and every scene the run opens shares it.
/// </summary>
/// <remarks>
/// Bus volumes, the random sequence, the save documents and a pending frame capture survive
/// transitions. Every member is fixed before the run starts or set by game code. The host writes only
/// the save stamps it restores and sets, and the development overlay raises scene-flow requests
/// through these same members.
/// <para>
/// At most one transition is pending at a time. Within a step the first transition request wins, and
/// an exit request replaces it. The host takes the pending transition after the step. Every
/// transition request after <see cref="RequestExit"/> throws.
/// </para>
/// </remarks>
public sealed class Run
{
    private bool _exitRequested;
    private string? _frameCapturePath;
    private SceneTransition? _transition;
    private SceneTransition? _prefetch;
    private object? _state;

    // The canvas a run uses when none is supplied, 1280 by 720 pixels.
    internal static Vector2 StandardCanvas { get; } = new(1280f, 720f);

    /// <summary>Starts a run with the default random seed, a 1280 by 720 canvas and linear sampling.</summary>
    public Run()
        : this(new RandomSource())
    {
    }

    /// <summary>
    /// Starts a run using <paramref name="random"/> and keeps that source for the run's lifetime. The
    /// canvas starts at 1280 by 720 pixels and sampling at <see cref="TextureSampling.Linear"/> until the
    /// game changes them.
    /// </summary>
    /// <param name="random">The deterministic random source shared by every scene this run opens.</param>
    public Run(RandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        Random = random;
        Audio = new AudioMixer();
        Rumble = new Rumble();
        Cursor = new Cursor();
        Saves = new SaveStore();
    }

    // Whether this run's scenes draw the engine's debug channels. On for the game's run. A host turns
    // it off on the run backing its own overlay, whose entities are not the game's.
    internal bool EmitsDebugDraw { get; init; } = true;

    // The render surface the host draws the world on, or null when it draws straight into the back
    // buffer. The camera resolves its canvas to world conversion through the same geometry.
    internal (int Width, int Height)? RenderResolution { get; init; }

    /// <summary>
    /// The screen layer's extent in canvas pixels, with the origin at the top-left corner and Y running
    /// down, defaulting to 1280 by 720.
    /// </summary>
    /// <remarks>
    /// A windowed run takes the canvas the game declared at boot, then the declared render resolution,
    /// then the window size the run was configured to open at. It does not follow a window the player
    /// resizes. The canvas scales to fit the surface that presents the frame. A
    /// <see cref="ScreenEntity"/> anchors and hit-tests against this extent. A camera fit that reveals
    /// more world than the canvas covers keeps the screen layer at this extent, centred in the surface
    /// the world was drawn on. The game may set it during the run for a UI-scale option: a larger canvas
    /// draws a smaller interface. Screen entities lay out against the new value from the next step. The
    /// host never refits the game's run.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either component is not greater than zero or is NaN.
    /// </exception>
    public Vector2 Canvas
    {
        get;

        set
        {
            if (value.X <= 0f || float.IsNaN(value.X) || value.Y <= 0f || float.IsNaN(value.Y))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A canvas component must be greater than zero and not NaN.");
            }

            field = value;
        }
    } = StandardCanvas;

    /// <summary>
    /// The sampling for world-space textures in any scene that sets none of its own, defaulting to
    /// <see cref="TextureSampling.Linear"/>.
    /// </summary>
    public TextureSampling Sampling { get; init; } = TextureSampling.Linear;

    /// <summary>
    /// The run's deterministic random source, the instance the run was built with.
    /// </summary>
    /// <remarks>
    /// A scene transition neither reseeds nor rewinds it. In a shell-built run it is stream 0 of the seed
    /// the shell configured. A domain whose draws must not disturb another takes its own stream, as
    /// <c>new RandomSource(Random.Seed, MyStreams.Map)</c>.
    /// </remarks>
    public RandomSource Random { get; }

    /// <summary>
    /// The run's audio mixer, owned by the engine and the same instance for the whole run.
    /// </summary>
    /// <remarks>
    /// A voice an outgoing scene starts keeps playing across a transition until its starter stops it.
    /// Bus volumes and bus pause state set once hold for every later scene. Use an
    /// <see cref="Capsule.Audio.AudioSource"/> for per-entity sound and this mixer for sound no entity
    /// owns. A scene constructor cannot reach the run. Level a bus from <c>OnStart</c> onward.
    /// </remarks>
    public AudioMixer Audio { get; }

    /// <summary>
    /// The run's gamepad rumble, owned by the engine and the same instance for the whole run.
    /// </summary>
    /// <remarks>
    /// A pulse an outgoing scene starts keeps playing across a transition until it ends or its starter
    /// stops it. A settings screen writes <see cref="Capsule.Input.Rumble.Volume"/>, and a pause menu
    /// calls <see cref="Capsule.Input.Rumble.Stop()"/> when it opens. The host reads the level after each
    /// step and writes it to the pad. The host rests the motors on focus loss, disconnect, exit and crash,
    /// and while the keyboard or mouse is the active device. A headless run reaches the same level with no
    /// pad.
    /// </remarks>
    public Rumble Rumble { get; }

    /// <summary>The run's mouse cursor, owned by the engine and the same instance for the whole run.</summary>
    public Cursor Cursor { get; }

    /// <summary>
    /// The run's save documents, one store shared by every scene it opens. The host restores it before
    /// the first scene composes, and settings are readable from the first start hook.
    /// </summary>
    public SaveStore Saves { get; }

    /// <summary>Attaches the game's one run-scoped object.</summary>
    /// <param name="state">The object to attach.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A run already holds one attached object.</exception>
    public void Attach<TState>(TState state)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_state is not null)
        {
            throw new InvalidOperationException("A run holds one attached object. Attach it once, at run start.");
        }

        _state = state;
    }

    /// <summary>The object <see cref="Attach{TState}"/> attached, as <typeparamref name="TState"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing is attached, or the attached object is not a <typeparamref name="TState"/>.
    /// </exception>
    public TState State<TState>()
        where TState : class
    {
        if (_state is null)
        {
            throw new InvalidOperationException("Nothing is attached to this run. Attach the game's object at run start.");
        }

        if (_state is not TState state)
        {
            throw new InvalidOperationException($"The run's attached object is a {_state.GetType()}, not a {typeof(TState)}.");
        }

        return state;
    }

    /// <summary>
    /// The run's input configuration, the one the shell built through
    /// <c>EngineBuilder.WithRunStart</c>. It stays live: a rebind applies from the next read and a
    /// deadzone change from the next sampled frame.
    /// </summary>
    public InputConfiguration Input { get; init; } = new();

    /// <summary>
    /// How many simulation seconds one wall second is worth, defaulting to 1.
    /// </summary>
    /// <remarks>
    /// A different pace makes the run step more or less often per wall second. The fixed step, each
    /// step's tick and each step's time stay identical at any pace. A run at 0.25 is the same run as at
    /// 1, played slower.
    /// <para>
    /// Read it only from host-facing code. A simulation that branches on it stops being a function of
    /// its snapshots, and a replay of it diverges. A headless run counts steps and ignores it. The
    /// per-frame step bound does not change. A pace that needs more steps than one frame allows runs that
    /// frame at its bound and drops the backlog.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not greater than zero, or is not finite.
    /// </exception>
    public double TimeScale
    {
        get;

        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The time scale must be finite and greater than zero.");
            }

            field = value;
        }
    } = 1;

    /// <summary>
    /// Asks the host to replace the current scene with <typeparamref name="TScene"/> after the
    /// current step.
    /// </summary>
    /// <param name="payload">State offered to the scene that opens.</param>
    public void RequestScene<TScene>(object? payload = null)
        where TScene : Scene =>
        TryRequest(SceneTransition.ToScene(typeof(TScene), payload));

    /// <summary>
    /// Asks the host to replace the current scene after the current step with the scene the named
    /// document backs, or with a plain <see cref="Scene"/> composed from it when no class claims it.
    /// </summary>
    /// <param name="name">The document's key under the scene root, without <c>.scene.json</c>.</param>
    /// <param name="payload">State offered to the scene that opens.</param>
    public void RequestScene(string name, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TryRequest(SceneTransition.ToName(name, payload));
    }

    /// <summary>Starts loading the media <typeparamref name="TScene"/> preloads after the current step.</summary>
    /// <remarks>
    /// Call it on intent, such as a highlighted menu item, not from a proximity sweep. The scene's
    /// constructor runs twice. At most one scene is prefetched, and a repeat does nothing.
    /// </remarks>
    public void PrefetchScene<TScene>()
        where TScene : Scene =>
        Prefetch(SceneTransition.ToScene(typeof(TScene), null));

    /// <summary>Prefetches the named document's scene, as <see cref="PrefetchScene{TScene}"/> does for a class.</summary>
    /// <param name="name">The document's key under the scene root, without <c>.scene.json</c>.</param>
    public void PrefetchScene(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Prefetch(SceneTransition.ToName(name, null));
    }

    /// <summary>Asks the host to reconstruct the current scene after the current step.</summary>
    public void RequestRestart() => TryRequest(SceneTransition.Restart(null, false));

    /// <summary>
    /// Asks the host to reconstruct the current scene with <paramref name="payload"/> after the
    /// current step.
    /// </summary>
    /// <param name="payload">State supplied to the reconstructed scene. Passing null supplies null.</param>
    public void RequestRestart(object? payload) => TryRequest(SceneTransition.Restart(payload, true));

    /// <summary>
    /// Asks the host to shut down after the current step. The exit replaces any pending transition
    /// and drops any pending frame capture and prefetch.
    /// </summary>
    /// <remarks>A second call does nothing, and nothing cancels it.</remarks>
    public void RequestExit()
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        _transition = SceneTransition.Exit();
        _frameCapturePath = null;
        _prefetch = null;
    }

    /// <summary>Whether game code has asked this run to end. Once true, it stays true.</summary>
    public bool ExitRequested => _exitRequested;

    /// <summary>
    /// The path of the frame capture waiting for the next drawn frame, or null when none is pending. The
    /// host clears it when it takes the request.
    /// </summary>
    public string? FrameCaptureRequested => _frameCapturePath;

    /// <summary>
    /// Asks the host to save the next frame it draws as a PNG at <paramref name="path"/>,
    /// overwriting whatever is there and creating the directory the path names.
    /// </summary>
    /// <param name="path">
    /// Where to write the PNG. A relative path resolves against the process working directory.
    /// </param>
    /// <remarks>
    /// The image is the surface the world was drawn on: the declared render resolution, or the back
    /// buffer when the run declares none. Requesting again
    /// before the host takes the request replaces the path, and the request survives a scene transition.
    /// A frame with no surface to draw on, such as a minimised window, leaves the request pending. A run
    /// with no graphics device (<c>RunHeadless</c>, or <c>--headless</c>) clears the request and writes
    /// nothing. A failed save writes no file, reports through <see cref="Capsule.Diagnostics.Log"/> at
    /// <see cref="Capsule.Diagnostics.LogLevel.Warning"/>, and drops the request instead of throwing
    /// into the frame loop. See <c>docs/input.md</c> for the host side.
    /// </remarks>
    public void CaptureFrame(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfExitRequested();

        _frameCapturePath = path;
    }

    internal bool TryTakeTransition(out SceneTransition transition)
    {
        if (_transition is not { } requested)
        {
            transition = default;
            return false;
        }

        _transition = null;
        transition = requested;
        return true;
    }

    internal bool TryTakePrefetch(out SceneTransition target)
    {
        if (_prefetch is not { } requested)
        {
            target = default;
            return false;
        }

        _prefetch = null;
        target = requested;
        return true;
    }

    internal bool TryTakeFrameCapture(out string path)
    {
        if (_frameCapturePath is not { } requested)
        {
            path = "";
            return false;
        }

        _frameCapturePath = null;
        path = requested;
        return true;
    }

    // The non-generic entry point the public request methods share. The host's development overlay uses
    // it to request a registered scene in whichever form the registry composes it from.
    internal bool TryRequest(in SceneTransition transition)
    {
        ThrowIfExitRequested();

        if (_transition is not null)
        {
            return false;
        }

        _transition = transition;
        return true;
    }

    // A later prefetch in the same step replaces an earlier one, as the host would.
    private void Prefetch(in SceneTransition target)
    {
        ThrowIfExitRequested();

        _prefetch = target;
    }

    private void ThrowIfExitRequested()
    {
        if (_exitRequested)
        {
            throw new InvalidOperationException("This run has already requested exit. Make no further requests on it.");
        }
    }
}
