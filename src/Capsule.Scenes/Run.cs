using System.Numerics;
using Capsule.Audio;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule;

/// <summary>
/// A run is one launch of the game from boot to exit as the simulation sees it. It owns the state
/// and requests shared by that run. One instance is installed on every scene before that scene
/// starts and shared by every scene the run opens, so bus volumes, the random sequence and a
/// pending frame capture persist across transitions. Everything on it is either fixed before the
/// run starts or set by game code; the host takes the requests game code raises and writes nothing
/// of its own into the game's run. The one exception is the development debug overlay, which
/// raises scene-flow requests on a developer's behalf through these same members.
/// <para>
/// At most one transition is pending. The first transition request wins within a step, while an
/// exit request replaces it. The host takes the pending transition after the step.
/// </para>
/// </summary>
public sealed class Run
{
    private bool _exitRequested;
    private string? _frameCapturePath;
    private SceneTransition? _transition;

    /// <summary>The canvas a run uses when none is supplied: 1280 by 720 pixels.</summary>
    public static Vector2 StandardCanvas { get; } = new(1280f, 720f);

    /// <summary>Starts a run with the default random seed, standard canvas and linear sampling.</summary>
    public Run()
        : this(new RandomSource())
    {
    }

    /// <summary>
    /// Starts a run using <paramref name="random"/>, retaining that source for the run's lifetime.
    /// The canvas is <see cref="StandardCanvas"/> and sampling is
    /// <see cref="TextureSampling.Linear"/> until set by the game.
    /// </summary>
    /// <param name="random">The deterministic random source shared by every scene this run opens.</param>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
    public Run(RandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        Random = random;
        Audio = new AudioMixer();
    }

    // Whether the scenes of this run draw the engine's debug channels. On for the game's run; a
    // host turns it off on a run it owns for its own overlay, whose entities are not the game's.
    internal bool EmitsDebugDraw { get; init; } = true;

    /// <summary>
    /// The screen layer's extent in canvas pixels, whose origin is its top-left corner and whose Y
    /// runs down. In a windowed run it is the canvas the game declared at boot; else the declared
    /// render resolution; else the window size the run was configured to open at. It never follows
    /// a window the player resizes: the canvas is scaled to fit whatever the frame is presented on.
    /// A <see cref="ScreenEntity"/> is anchored and hit-tested against it, and a camera fit that
    /// reveals more world than the canvas holds leaves the screen layer this extent, centred in
    /// what the world was drawn on. The value is shared by every scene the run opens. The game may
    /// set it during the run — a larger canvas is a smaller interface, which is what a UI-scale
    /// option moves — and every screen entity is laid out against the new value from the next
    /// step. A host refits only a run it owns itself, such as the development debug overlay's,
    /// never the game's.
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
    /// The default filtering policy for world-space textures, shared by scenes that set no own
    /// policy. Linear by default.
    /// </summary>
    public TextureSampling Sampling { get; init; } = TextureSampling.Linear;

    /// <summary>
    /// The run's deterministic random source, the instance the run was built with and held for the
    /// whole run, so a scene transition neither reseeds nor rewinds it; in a shell-built run it is
    /// stream 0 of the seed the shell configured. A domain whose draws must not move another's
    /// takes its own stream — <c>new RandomSource(Random.Seed, MyStreams.Map)</c>.
    /// </summary>
    public RandomSource Random { get; }

    /// <summary>
    /// The run's audio mixer, engine-owned and the same instance for the whole run, so a voice an
    /// outgoing scene starts keeps playing across a transition unless whatever started it stops it,
    /// and bus volumes and bus pause state set once hold for every scene after. An
    /// <see cref="Capsule.Audio.AudioSource"/> on an entity is the per-entity way in; this is the
    /// way to play a sound no entity owns. Installed before a scene starts, so a scene constructor
    /// cannot level a bus.
    /// </summary>
    public AudioMixer Audio { get; }

    /// <summary>
    /// Host pace: the simulation seconds a wall second is worth. One by default. The run steps
    /// fewer or more times per wall second and nothing the simulation is handed changes — the fixed
    /// step, each step's tick and each step's time are the same at any pace — so a run at 0.25x is
    /// the same run as at 1x, played slower.
    /// <para>
    /// It is never simulation input. A simulation that branches on it is no longer a function of
    /// its snapshots, and a driven run of it diverges; read it only from host-facing code. A
    /// headless run ignores it, being counted in steps rather than wall time. The per-frame step
    /// bound is unchanged, so a pace that asks for more steps than a frame may run leaves that
    /// frame at its bound and drops the backlog rather than running faster.
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
    /// <exception cref="InvalidOperationException">Exit has already been requested.</exception>
    public void RequestScene<TScene>(object? payload = null)
        where TScene : Scene =>
        TryRequest(SceneTransition.ToScene(typeof(TScene), payload));

    /// <summary>
    /// Asks the host to replace the current scene with the scene the named document backs after
    /// the current step, or a plain <see cref="Scene"/> composed from it when no class claims it.
    /// </summary>
    /// <param name="name">The document's key under the scene root, without <c>.scene.json</c>.</param>
    /// <param name="payload">State offered to the scene that opens.</param>
    /// <exception cref="ArgumentException">The name is null or blank.</exception>
    /// <exception cref="InvalidOperationException">Exit has already been requested.</exception>
    public void RequestScene(string name, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TryRequest(SceneTransition.ToName(name, payload));
    }

    /// <summary>Asks the host to reconstruct the current scene after the current step.</summary>
    /// <exception cref="InvalidOperationException">Exit has already been requested.</exception>
    public void RequestRestart() => TryRequest(SceneTransition.Restart(null, false));

    /// <summary>
    /// Asks the host to reconstruct the current scene with <paramref name="payload"/> after the
    /// current step.
    /// </summary>
    /// <param name="payload">State supplied to the reconstructed scene, including null when supplied.</param>
    /// <exception cref="InvalidOperationException">Exit has already been requested.</exception>
    public void RequestRestart(object? payload) => TryRequest(SceneTransition.Restart(payload, true));

    /// <summary>
    /// Asks the host to shut down after the current step. It replaces any pending transition and
    /// drops any pending frame capture, is idempotent, and is never cleared.
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

    /// <summary>Whether game code has requested that this run end; true once set and never cleared.</summary>
    public bool ExitRequested => _exitRequested;

    /// <summary>
    /// The path of the frame capture request waiting for the next drawn frame, or null when none
    /// is pending. A later request replaces an earlier one, the host clears it when it takes the
    /// request, it remains with this run across a scene transition, and an exit drops it.
    /// </summary>
    public string? FrameCaptureRequested => _frameCapturePath;

    /// <summary>
    /// Asks the host to save the next frame it draws as a PNG at <paramref name="path"/>,
    /// overwriting whatever is there and creating the directory the path names.
    /// </summary>
    /// <param name="path">
    /// Where to write the PNG; a relative path resolves against the process working directory.
    /// </param>
    /// <remarks>
    /// The image is the surface the world was drawn on — the declared render resolution, or the
    /// back buffer where the run declares none — so it never follows the window's own size.
    /// Requesting again before the host takes the request replaces the path, and the request
    /// remains with this run across a scene transition. A frame with no surface to draw on, as a
    /// minimised window has, leaves it pending; a run with no graphics device at all
    /// (<c>RunHeadless</c>, or <c>--headless</c>) clears it and writes nothing. A save that fails
    /// writes no file, reports itself through <see cref="Capsule.Diagnostics.Log"/> at
    /// <see cref="Capsule.Diagnostics.LogLevel.Warning"/>, and drops the request rather than
    /// throwing into the frame loop. See <c>docs/headless-play.md</c> for the host side.
    /// </remarks>
    /// <exception cref="ArgumentException">The path is null, empty or blank.</exception>
    /// <exception cref="InvalidOperationException">Exit has already been requested.</exception>
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

    // The non-generic entry the public requests share; the host's development overlay requests a
    // registered scene by whichever form the registry composes it from.
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
            throw new InvalidOperationException("A run that has requested exit cannot accept another request.");
        }
    }
}
