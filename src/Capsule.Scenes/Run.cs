using System.Numerics;
using Capsule.Audio;
using Capsule.Input;
using Capsule.Persistence;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule;

/// <summary>
/// A run is one launch of the game from boot to exit, as the simulation sees it. It holds the state and
/// requests shared across that launch. The engine installs one instance on every scene before the scene
/// starts, and every scene the run opens shares it, so bus volumes, the random sequence, the save
/// documents and a pending frame capture survive transitions. Every member is either fixed before the
/// run starts or set by game code. The host writes nothing of its own into the game's run except the
/// save stamps it restores and sets. The development overlay is the exception: it raises scene-flow
/// requests for a developer through these same members.
/// <para>
/// At most one transition is pending at a time. Within a step, the first transition request wins, an
/// exit request replaces it, and the host takes the pending transition after the step. Any request made
/// after <see cref="RequestExit"/> throws, because a run that is ending accepts no more.
/// </para>
/// </summary>
public sealed class Run
{
    private bool _exitRequested;
    private string? _frameCapturePath;
    private SceneTransition? _transition;

    /// <summary>The canvas a run uses when none is supplied, 1280 by 720 pixels.</summary>
    public static Vector2 StandardCanvas { get; } = new(1280f, 720f);

    /// <summary>Starts a run with the default random seed, standard canvas and linear sampling.</summary>
    public Run()
        : this(new RandomSource())
    {
    }

    /// <summary>
    /// Starts a run using <paramref name="random"/> and keeps that source for the run's lifetime. The
    /// canvas starts at <see cref="StandardCanvas"/> and sampling at
    /// <see cref="TextureSampling.Linear"/> until the game changes them.
    /// </summary>
    /// <param name="random">The deterministic random source shared by every scene this run opens.</param>
    public Run(RandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        Random = random;
        Audio = new AudioMixer();
        Rumble = new Rumble();
        Saves = new SaveStore();
    }

    // Whether this run's scenes draw the engine's debug channels. On for the game's run. A host turns
    // it off on the run backing its own overlay, whose entities are not the game's.
    internal bool EmitsDebugDraw { get; init; } = true;

    /// <summary>
    /// The screen layer's extent in canvas pixels, with the origin at the top-left corner and Y running
    /// down. A windowed run takes the canvas the game declared at boot, falling back to the declared
    /// render resolution, then to the window size the run was configured to open at. It does not follow
    /// a window the player resizes. The canvas is scaled to fit whatever surface presents the frame. A
    /// <see cref="ScreenEntity"/> is anchored and hit-tested against this extent. A camera fit that
    /// reveals more world than the canvas covers keeps the screen layer at this extent, centred in the
    /// surface the world was drawn on. Every scene the run opens shares the value. The game may set it
    /// during the run, which is how a UI-scale option works: a larger canvas draws a smaller interface.
    /// Every screen entity lays out against the new value from the next step. A host refits
    /// only a run it owns, such as the development overlay's, and never the game's.
    /// </summary>
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
    /// The default filtering policy for world-space textures, used by any scene that sets none of its
    /// own. Linear by default.
    /// </summary>
    public TextureSampling Sampling { get; init; } = TextureSampling.Linear;

    /// <summary>
    /// The run's deterministic random source. It is the instance the run was built with and lives for
    /// the whole run. A scene transition neither reseeds nor rewinds it. In a shell-built run it is
    /// stream 0 of the seed the shell configured. A domain whose draws must not disturb another takes
    /// its own stream, as <c>new RandomSource(Random.Seed, MyStreams.Map)</c>.
    /// </summary>
    public RandomSource Random { get; }

    /// <summary>
    /// The run's audio mixer, owned by the engine and the same instance for the whole run. A voice an
    /// outgoing scene starts keeps playing across a transition until its starter stops it, and bus
    /// volumes and bus pause state set once hold for every later scene. Use an
    /// <see cref="Capsule.Audio.AudioSource"/> for per-entity sound and this mixer for sound no entity
    /// owns. The engine installs it before a scene starts, which is too late for a scene constructor to
    /// level a bus.
    /// </summary>
    public AudioMixer Audio { get; }

    /// <summary>
    /// The run's gamepad rumble, owned by the engine and the same instance for the whole run. A pulse
    /// an outgoing scene starts keeps playing across a transition until it ends or its starter stops
    /// it. A settings screen writes <see cref="Capsule.Input.Rumble.Volume"/>, and a pause menu calls
    /// <see cref="Capsule.Input.Rumble.Stop()"/> when it opens. The host reads the level after each
    /// step and writes it to the pad, and a headless run reaches the same level with no pad.
    /// </summary>
    public Rumble Rumble { get; }

    /// <summary>
    /// The run's save documents, one store shared by every scene it opens. The host restores it before
    /// the first scene composes, so settings are readable from the first start hook.
    /// </summary>
    public SaveStore Saves { get; }

    /// <summary>
    /// How many simulation seconds one wall second is worth. One by default. Changing it makes the run
    /// step more or less often per wall second, and the simulation sees no difference, because the fixed
    /// step, each step's tick and each step's time are identical at any pace. A run at 0.25x is the same
    /// run as at 1x, played slower.
    /// <para>
    /// This is not simulation input. A simulation that branches on it stops being a function of its
    /// snapshots and a replay of it diverges, so read it only from host-facing code. A headless run
    /// ignores it, because it counts steps instead of wall time. The per-frame step bound does not
    /// change. A pace demanding more steps than one frame allows leaves that frame at its bound and
    /// drops the backlog instead of running faster.
    /// </para>
    /// </summary>
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

    /// <summary>Asks the host to reconstruct the current scene after the current step.</summary>
    public void RequestRestart() => TryRequest(SceneTransition.Restart(null, false));

    /// <summary>
    /// Asks the host to reconstruct the current scene with <paramref name="payload"/> after the
    /// current step.
    /// </summary>
    /// <param name="payload">State supplied to the reconstructed scene. Passing null supplies null.</param>
    public void RequestRestart(object? payload) => TryRequest(SceneTransition.Restart(payload, true));

    /// <summary>
    /// Asks the host to shut down after the current step. It replaces any pending transition, drops any
    /// pending frame capture, does nothing on a second call, and cannot be cancelled.
    /// </summary>
    public void RequestExit()
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        _transition = SceneTransition.Exit();
        _frameCapturePath = null;
    }

    /// <summary>Whether game code has asked this run to end. Once true, it stays true.</summary>
    public bool ExitRequested => _exitRequested;

    /// <summary>
    /// The path of the frame capture waiting for the next drawn frame, or null when none is pending. A
    /// later request replaces an earlier one, the host clears it when it takes the request, it survives
    /// a scene transition, and an exit request drops it.
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
    /// The image is the surface the world was drawn on, which is the declared render resolution, or the
    /// back buffer when the run declares none, so it does not follow the window's size. Requesting again
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

    private void ThrowIfExitRequested()
    {
        if (_exitRequested)
        {
            throw new InvalidOperationException("This run has already requested exit. Make no further requests on it.");
        }
    }
}
