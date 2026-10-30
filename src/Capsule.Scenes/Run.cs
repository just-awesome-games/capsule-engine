using System.Numerics;
using Capsule.Audio;
using Capsule.Input;
using Capsule.Localization;
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
/// Bus volumes, the random sequence, the save documents, the clock and a pending frame capture survive
/// transitions. Every member is fixed before the run starts or set by game code, except the clock, which
/// each step advances. The host writes only
/// the save stamps it restores and sets, and the debug overlay raises scene-flow requests
/// through these same members.
/// <para>
/// At most one transition is pending at a time. Within a step the first transition request wins, and
/// an exit request replaces it. The host takes the pending transition after the step. Every
/// transition request after <see cref="RequestExit"/> throws.
/// </para>
/// </remarks>
public sealed class Run
{
    // The canvas a run uses when none is supplied, 1280 by 720 pixels.
    internal static Vector2 StandardCanvas { get; } = new(1280f, 720f);

    private SceneTransition? _transition;
    private SceneTransition? _prefetch;
    private object? _state;

    // Whether a step has begun, after which a language request waits for the next one.
    private bool _stepped;
    private bool _languagePending;

    // The step length the last step ran at. Before the first step the clock reads 0 whatever it is.
    private double _stepSeconds;

    // The key and arguments GetText fills, and the buffer it decodes them into, both kept for the next call.
    // A call holds _keyedText out of the field while it fills it.
    private KeyedText? _keyedText;
    private char[] _textBuffer = [];

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
    public Vector2 Canvas
    {
        get;

        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || value.X <= 0f || value.Y <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A canvas component must be finite and greater than zero.");
            }

            field = value;
        }
    } = StandardCanvas;

    /// <summary>
    /// The language keyed text shows, as a BCP 47 tag such as <c>pt-BR</c>, or null, the default, for each
    /// string table's fallback language.
    /// </summary>
    /// <remarks>
    /// The getter returns the latest request at once, for a game to save. A request made before the run's
    /// first step applies at once, and one made after it applies from the start of the next step. A tag no table ships
    /// shows the closest shipped language, else the fallback, and never throws. A windowed run no driver
    /// plays starts in the shipped language closest to the player's preference. A headless or driven run
    /// starts in the fallback language, and a test shows the same text on every machine.
    /// </remarks>
    public string? Language
    {
        get;

        set
        {
            if (value is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
            }

            if (string.Equals(value, field, StringComparison.Ordinal))
            {
                return;
            }

            field = value;
            if (_stepped)
            {
                _languagePending = true;
            }
            else
            {
                ApplyLanguage();
            }
        }
    }

    // The language keyed text resolves against, the last request applied.
    internal string? ActiveLanguage { get; private set; }

    // Counts every applied language request. A keyed label re-resolves when it changes.
    internal int LanguageVersion { get; private set; }

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
    /// <c>EngineBuilder.WithRandomSeed</c> describes. A domain whose draws must not disturb another takes its own stream, as
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
    /// while the keyboard or mouse is the active device, and while the debug overlay holds the run.
    /// A headless run reaches the same level with no pad.
    /// </remarks>
    public Rumble Rumble { get; }

    /// <summary>The run's mouse cursor, owned by the engine and the same instance for the whole run.</summary>
    public Cursor Cursor { get; }

    /// <summary>
    /// The run's window and frame pacing, the one the shell configured through its builder. A settings
    /// screen changes it, and the host applies each change after the frame's steps.
    /// </summary>
    public Display Display { get; init; } = new();

    /// <summary>
    /// The run's save documents, one store shared by every scene it opens. The host restores it before
    /// the first scene composes, and settings are readable from the first start hook.
    /// </summary>
    public SaveStore Saves { get; }

    /// <summary>Whether game code has asked this run to end. Once true, it stays true.</summary>
    public bool ExitRequested { get; private set; }

    /// <summary>
    /// The path of the frame capture waiting for the next drawn frame, or null when none is pending. The
    /// host clears it when it takes the request.
    /// </summary>
    public string? FrameCaptureRequested { get; private set; }

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
    /// It is the whole game's speed, such as an assist option that plays everything slower, menus and
    /// interface included. A game may set it from a step and reads it only from host-facing code. A
    /// simulation that branches on it stops being a function of its snapshots, and a replay of it
    /// diverges. A pause is <c>Scene.Paused</c>, a hit-stop is <c>Scene.Freeze</c>, and slow motion
    /// inside the game is its entities' own stepping, never a pace. A headless run counts steps and
    /// ignores it. The per-frame step bound does not change. A pace that needs more steps than one frame
    /// allows runs that frame at its bound and drops the backlog.
    /// </para>
    /// </remarks>
    public double TimeScale
    {
        get;

        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The time scale must be finite and greater than zero. To hold the game, set Scene.Paused. To hold a few steps, call Scene.Freeze.");
            }

            field = value;
        }
    } = 1;

    /// <summary>
    /// The index of the step running, and between steps the number of steps the run has run. Inside a
    /// step it equals <see cref="StepContext.Tick"/>.
    /// </summary>
    /// <remarks>
    /// It reads 0 in a <c>EngineBuilder.WithRunStart</c> hook and the run's whole length in steps in a
    /// <c>EngineBuilder.WithRunEnd</c> hook. A scene transition does not reset it.
    /// </remarks>
    public long Tick { get; private set; }

    /// <summary>
    /// Simulated seconds at <see cref="Tick"/>, not wall clock. Inside a step it equals
    /// <see cref="StepContext.TotalSeconds"/>.
    /// </summary>
    /// <remarks>
    /// It reads 0 in a <c>EngineBuilder.WithRunStart</c> hook and the run's whole simulated length in a
    /// <c>EngineBuilder.WithRunEnd</c> hook.
    /// </remarks>
    public double TotalSeconds => Tick * _stepSeconds;

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

    /// <summary>Attaches the game's one run-scoped object.</summary>
    /// <param name="state">The object to attach.</param>
    /// <exception cref="InvalidOperationException">A run already holds one attached object.</exception>
    public void Attach<TState>(TState state)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_state is not null)
        {
            throw new InvalidOperationException("A run holds one attached object. Attach it once, at run start. Hold every run-wide object as a member of that one object.");
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
    /// Asks the host to replace the current scene with <typeparamref name="TScene"/> after the
    /// current step.
    /// </summary>
    /// <param name="payload">State offered to the scene that opens.</param>
    public void RequestScene<TScene>(IScenePayload? payload = null)
        where TScene : Scene =>
        TryRequest(SceneTransition.ToScene(typeof(TScene), payload));

    /// <summary>
    /// Asks the host to replace the current scene after the current step with the scene the named
    /// document backs, or with a plain <see cref="Scene"/> composed from it when no class claims it.
    /// </summary>
    /// <param name="scene">The document's key, a <c>CapsuleAssets.Scenes</c> member.</param>
    /// <param name="payload">State offered to the scene that opens.</param>
    public void RequestScene(SceneKey scene, IScenePayload? payload = null) =>
        TryRequest(SceneTransition.ToName(scene.Required(nameof(scene)), payload));

    /// <summary>
    /// Starts loading the media <typeparamref name="TScene"/> preloads when it opens with
    /// <paramref name="payload"/>, after the current step.
    /// </summary>
    /// <remarks>
    /// Call it on intent, such as a highlighted menu item, not from a proximity sweep. The scene's
    /// constructor runs twice. At most one scene is prefetched, and a later call replaces an earlier one
    /// unless it names the same scene and an equal payload. A request opening the scene with another payload
    /// preloads what that payload declares, and the prefetched media it does not declare is released.
    /// </remarks>
    /// <param name="payload">The payload the scene's <c>CollectAssets</c> reads as its <c>EntryPayload</c>.</param>
    public void PrefetchScene<TScene>(IScenePayload? payload = null)
        where TScene : Scene =>
        Prefetch(SceneTransition.ToScene(typeof(TScene), payload));

    /// <summary>Prefetches the named document's scene, as <see cref="PrefetchScene{TScene}"/> does for a class.</summary>
    /// <param name="scene">The document's key, a <c>CapsuleAssets.Scenes</c> member.</param>
    /// <param name="payload">The payload the scene's <c>CollectAssets</c> reads as its <c>EntryPayload</c>.</param>
    public void PrefetchScene(SceneKey scene, IScenePayload? payload = null) =>
        Prefetch(SceneTransition.ToName(scene.Required(nameof(scene)), payload));

    /// <summary>Asks the host to reconstruct the current scene after the current step.</summary>
    public void RequestRestart() => TryRequest(SceneTransition.Restart(null, false));

    /// <summary>
    /// Asks the host to reconstruct the current scene with <paramref name="payload"/> after the
    /// current step.
    /// </summary>
    /// <param name="payload">State supplied to the reconstructed scene. Passing null supplies null.</param>
    public void RequestRestart(IScenePayload? payload) => TryRequest(SceneTransition.Restart(payload, true));

    /// <summary>
    /// Asks the host to shut down after the current step. The exit replaces any pending transition
    /// and drops any pending frame capture and prefetch.
    /// </summary>
    /// <remarks>A second call does nothing, and nothing cancels it.</remarks>
    public void RequestExit()
    {
        if (ExitRequested)
        {
            return;
        }

        ExitRequested = true;
        _transition = SceneTransition.Exit();
        FrameCaptureRequested = null;
        _prefetch = null;
    }

    /// <summary>
    /// Asks the host to save the next frame it draws as a PNG at <paramref name="path"/>,
    /// overwriting whatever is there and creating the directory the path names.
    /// </summary>
    /// <param name="path">
    /// Where to write the PNG. A relative path resolves against the process working directory.
    /// </param>
    /// <remarks>
    /// The image is the surface the world was drawn on: the declared render resolution, or the window's
    /// frame at its extent in pixels when the run declares none. It never shows the debug overlay. A
    /// capture is not tied to the step that requested it. The host
    /// may run more steps before it next draws, and the frame shows the last of them interpolated from
    /// the one before. An event that lasts one step can be missing from the image. Requesting again
    /// before the host takes the request replaces the path, and the request survives a scene transition.
    /// A frame with no surface to draw on, such as a minimised window, leaves the request pending. A run
    /// with no window (<c>RunHeadless</c>, or <c>--headless</c>, under <c>--capture</c> too) clears the
    /// request and writes nothing. A failed save writes no file, reports through <see cref="Capsule.Diagnostics.Log"/> at
    /// <see cref="Capsule.Diagnostics.LogLevel.Warning"/>, and drops the request instead of throwing
    /// into the frame loop. See <c>docs/debugging.md</c> for the host side.
    /// </remarks>
    public void CaptureFrame(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfExitRequested();

        FrameCaptureRequested = path;
    }

    /// <summary>Returns <paramref name="text"/> in the run's active language, the one its labels show.</summary>
    /// <remarks>
    /// The string keeps this language after a later <see cref="Language"/> request. Text on screen belongs in
    /// a <see cref="Label"/>, which follows the request and allocates nothing. Once warm, a call allocates
    /// only the string it returns.
    /// </remarks>
    /// <example>
    /// <code>
    /// Log.Info(Run.GetText(CapsuleStrings.Menu.Begin));
    /// </code>
    /// </example>
    /// <param name="text">A <c>CapsuleStrings</c> member taking no arguments.</param>
    public string GetText(TextKey text)
    {
        KeyedText keyed = TakeKeyedText();
        keyed.Set(text);

        return KeepAndDecode(keyed);
    }

    /// <summary>Returns <paramref name="text"/> in the run's active language with its one placeholder filled.</summary>
    /// <remarks>
    /// The argument fills as <see cref="Label.SetText{T0}(TextKey, T0)"/> fills it, and the string keeps
    /// this language as <see cref="GetText(TextKey)"/> states.
    /// </remarks>
    /// <example>
    /// <code>
    /// string speed = Run.GetText(CapsuleStrings.Options.Speed, CapsuleStrings.Options.Fast);
    /// </code>
    /// </example>
    /// <param name="text">A <c>CapsuleStrings</c> member taking one argument.</param>
    /// <param name="arg0">The value of the text's first placeholder.</param>
    public string GetText<T0>(TextKey text, T0 arg0)
    {
        KeyedText keyed = TakeKeyedText();
        keyed.Set(text, arg0);

        return KeepAndDecode(keyed);
    }

    /// <summary>Returns <paramref name="text"/> in the run's active language with its two placeholders filled.</summary>
    /// <remarks>Each argument fills as <see cref="GetText{T0}(TextKey, T0)"/> fills one.</remarks>
    /// <param name="text">A <c>CapsuleStrings</c> member taking two arguments.</param>
    /// <param name="arg0">The value of the text's first placeholder.</param>
    /// <param name="arg1">The value of the text's second placeholder.</param>
    public string GetText<T0, T1>(TextKey text, T0 arg0, T1 arg1)
    {
        KeyedText keyed = TakeKeyedText();
        keyed.Set(text, arg0, arg1);

        return KeepAndDecode(keyed);
    }

    /// <summary>Returns <paramref name="text"/> in the run's active language with its three placeholders filled.</summary>
    /// <remarks>Each argument fills as <see cref="GetText{T0}(TextKey, T0)"/> fills one.</remarks>
    /// <param name="text">A <c>CapsuleStrings</c> member taking three arguments.</param>
    /// <param name="arg0">The value of the text's first placeholder.</param>
    /// <param name="arg1">The value of the text's second placeholder.</param>
    /// <param name="arg2">The value of the text's third placeholder.</param>
    public string GetText<T0, T1, T2>(TextKey text, T0 arg0, T1 arg1, T2 arg2)
    {
        KeyedText keyed = TakeKeyedText();
        keyed.Set(text, arg0, arg1, arg2);

        return KeepAndDecode(keyed);
    }

    // Opens a step: the clock reads the step's tick, and a language request made since the last one
    // applies from here.
    internal void BeginStep(in StepContext context)
    {
        Tick = context.Tick;
        _stepSeconds = context.StepSeconds;
        _stepped = true;
        if (_languagePending)
        {
            _languagePending = false;
            ApplyLanguage();
        }
    }

    // Closes a step that completed: between steps the clock reads the count of steps run.
    internal void EndStep() => Tick++;

    internal bool TryTakeTransition(out SceneTransition transition)
    {
        bool pending = _transition.HasValue;
        transition = _transition.GetValueOrDefault();
        _transition = null;
        return pending;
    }

    internal bool TryTakePrefetch(out SceneTransition target)
    {
        bool pending = _prefetch.HasValue;
        target = _prefetch.GetValueOrDefault();
        _prefetch = null;
        return pending;
    }

    internal bool TryTakeFrameCapture(out string path)
    {
        path = FrameCaptureRequested ?? "";
        FrameCaptureRequested = null;
        return path.Length > 0;
    }

    // The non-generic entry point the public request methods share. The host's debug overlay uses
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

    // Takes the kept KeyedText out of the run for one call. A GetText that an argument's formatting makes
    // during the call finds none and builds its own, which leaves the outer call's key and arguments intact.
    private KeyedText TakeKeyedText()
    {
        KeyedText keyed = _keyedText ?? new KeyedText();
        _keyedText = null;

        return keyed;
    }

    // Keeps keyed for the next call, decodes it in the active language into the kept buffer, and copies it
    // out as the one allocation. Decoding formats no argument, and no other call reaches the buffer before the copy.
    private string KeepAndDecode(KeyedText keyed)
    {
        _keyedText = keyed;
        int length = keyed.Decode(ActiveLanguage, ref _textBuffer);

        return new string(_textBuffer, 0, length);
    }

    private void ApplyLanguage()
    {
        ActiveLanguage = Language;
        LanguageVersion++;
    }

    private void ThrowIfExitRequested()
    {
        if (ExitRequested)
        {
            throw new InvalidOperationException("This run has already requested exit. Make no further requests on it.");
        }
    }
}
