using Capsule.Assets;
using Capsule.Audio;

namespace Capsule.Scenes.Audio;

/// <summary>
/// Plays one clip for its entity, holding the voice so the entity can stop, pause and re-level it.
/// The clip is declared as a preload, and the voice is stopped when the entity leaves the scene —
/// so a transition silences what a scene's entities were playing while anything started through
/// <see cref="Scene.Audio"/> plays on.
/// <para>
/// Capsule mixes no position into gain: this is a handle on a voice, not a point in space.
/// </para>
/// </summary>
/// <param name="clip">The clip <see cref="Play()"/> starts.</param>
public sealed class AudioSource(AudioClip clip) : Component
{
    // The mixer the live voice belongs to, kept so that a voice is stopped by the mixer that
    // started it even once the entity has left the scene that reached it.
    private AudioMixer? _playing;

    private Voice _voice;
    private float _volume = 1f;
    private float _pitch = 1f;
    private float _pan;

    /// <summary>The clip <see cref="Play()"/> starts and <see cref="Component.CollectAssets"/> declares; read at each play.</summary>
    public AudioClip Clip { get; set; } = clip;

    /// <summary>The bus this source's voices mix and pause through; <see cref="AudioBus.Master"/> by default. Read at each play.</summary>
    public AudioBus Bus { get; set; }

    /// <summary>
    /// This source's own linear amplitude in [0, 1]; 1 by default. Applied to the live voice at
    /// once, and to every voice played afterwards.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside [0, 1] or is not a number.</exception>
    public float Volume
    {
        get => _volume;
        set
        {
            AudioMixer.RequireVolume(value, nameof(value));
            _volume = value;
            _playing?.SetVolume(_voice, value);
        }
    }

    /// <summary>
    /// Playback-rate multiplier, positive and finite; 1 by default. Applied to the live voice at
    /// once, and to every voice played afterwards.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public float Pitch
    {
        get => _pitch;
        set
        {
            AudioMixer.RequirePitch(value, nameof(value));
            _pitch = value;
            _playing?.SetPitch(_voice, value);
        }
    }

    /// <summary>
    /// Where this source's voices sit between the speakers, in [-1, 1]: -1 hard left, 0 centred, 1
    /// hard right; centred by default. Applied to the live voice at once, and to every voice played
    /// afterwards. A mono clip pans across the whole field; a stereo one is rotated within it where
    /// the device supports that.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside [-1, 1] or is not a number.</exception>
    public float Pan
    {
        get => _pan;
        set
        {
            AudioMixer.RequirePan(value, nameof(value));
            _pan = value;
            _playing?.SetPan(_voice, value);
        }
    }

    /// <summary>
    /// The clip time this source's voice is at on the step being taken, in seconds from the clip's
    /// start; 0 when it owns no voice. Mirrors Unity's <c>AudioSource.time</c>, and moves in whole
    /// simulation steps.
    /// </summary>
    public double Time => _playing?.GetTime(_voice) ?? 0.0;

    /// <summary>
    /// Whether <see cref="Play()"/> starts a voice that repeats forever; read at each play. Where the
    /// clip carries an <see cref="AudioClip.LoopRegion"/>, the voice plays from the clip's beginning
    /// to the region's end and then repeats the region; <see cref="PlayOneShot"/> never loops and so
    /// always plays a clip to its end. The region repeated is the clip's own
    /// <see cref="AudioClip.LoopRegion"/>, whether the build read it out of the audio file or the
    /// game set it on the clip, and a clip carrying no region repeats whole.
    /// </summary>
    public bool Loop { get; set; }

    /// <summary>Whether the source plays itself in <see cref="Component.OnStart"/>.</summary>
    public bool PlayOnStart { get; set; }

    /// <summary>
    /// Whether this source still owns a voice: sounding, or held by its own pause or by its bus's.
    /// False before the first <see cref="Play()"/> and once the voice has ended — stopped, expired or
    /// stolen. Ownership rather than audibility, and the predicate to ask before starting another;
    /// <see cref="IsPlaying"/> and <see cref="IsPaused"/> partition it.
    /// </summary>
    public bool IsLive => _playing?.IsLive(_voice) ?? false;

    /// <summary>Whether this source's voice is live and sounding; false while it is held.</summary>
    public bool IsPlaying => _playing?.IsPlaying(_voice) ?? false;

    /// <summary>Whether this source's voice is live and held, by its own pause or by its bus's.</summary>
    public bool IsPaused => _playing?.IsPaused(_voice) ?? false;

    /// <summary>
    /// Starts <see cref="Clip"/> from the beginning, stopping whatever this source was already
    /// playing. Where every voice in the mixer is a live loop, nothing starts.
    /// </summary>
    /// <exception cref="InvalidOperationException">This source is on no entity in a started scene.</exception>
    public void Play() => Play(0.0);

    /// <summary>
    /// Starts <see cref="Clip"/> from <paramref name="startSeconds"/>, stopping whatever this source
    /// was already playing. Where every voice in the mixer is a live loop, nothing starts.
    /// </summary>
    /// <param name="startSeconds">
    /// Clip time to begin at, in seconds from the clip's start: at or after zero, and before the
    /// clip's duration unless it is zero. Godot's <c>play(from_position)</c> reads this way.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startSeconds"/> is negative, not finite, or at or past the clip's duration.</exception>
    /// <exception cref="InvalidOperationException">This source is on no entity in a started scene.</exception>
    public void Play(double startSeconds)
    {
        AudioMixer mixer = Mixer();

        mixer.Stop(_voice);
        _playing = mixer;
        _voice = mixer.Play(new AudioPlayback(Clip)
        {
            Bus = Bus,
            Volume = _volume,
            Pitch = _pitch,
            Pan = _pan,
            StartSeconds = startSeconds,
            Loop = Loop,
        });
    }

    /// <summary>
    /// Plays <paramref name="clip"/> as a separate one-shot on this source's bus and at its
    /// <see cref="Pan"/>, with <see cref="Volume"/> and <see cref="Pitch"/> multiplied by the scales
    /// given, leaving this source's own <see cref="Volume"/>, <see cref="Pitch"/> and voice alone.
    /// The voice is not tracked here; the caller holds it. Drawing <paramref name="pitchScale"/> from
    /// <see cref="Component.Random"/> over a small range — 0.95 to 1.05, say — keeps a repeated
    /// footfall or attack from reading as the same sample twice.
    /// </summary>
    /// <param name="clip">The clip to play once.</param>
    /// <param name="volumeScale">Factor on <see cref="Volume"/> for this play, itself in [0, 1].</param>
    /// <param name="pitchScale">Factor on <see cref="Pitch"/> for this play, positive and finite.</param>
    /// <returns>The voice started, or <see cref="Voice.None"/> when the mixer had none to give.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="volumeScale"/> is outside [0, 1] or is not a number, or
    /// <paramref name="pitchScale"/> is not positive and finite.
    /// </exception>
    /// <exception cref="InvalidOperationException">This source is on no entity in a started scene.</exception>
    public Voice PlayOneShot(AudioClip clip, float volumeScale = 1f, float pitchScale = 1f)
    {
        AudioMixer.RequireVolume(volumeScale, nameof(volumeScale));
        AudioMixer.RequirePitch(pitchScale, nameof(pitchScale));

        return Mixer().Play(new AudioPlayback(clip)
        {
            Bus = Bus,
            Volume = _volume * volumeScale,
            Pitch = _pitch * pitchScale,
            Pan = _pan,
        });
    }

    /// <summary>Ends this source's voice. Does nothing when it is playing none.</summary>
    public void Stop()
    {
        _playing?.Stop(_voice);
        _playing = null;
        _voice = Voice.None;
    }

    /// <summary>Holds this source's voice where it is. Does nothing when it is playing none.</summary>
    public void Pause() => _playing?.Pause(_voice);

    /// <summary>Continues this source's held voice. Does nothing when it is playing none.</summary>
    public void Resume() => _playing?.Resume(_voice);

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (Clip != default)
        {
            assets.Add(Clip);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnStart()
    {
        if (PlayOnStart)
        {
            Play();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene() => Stop();

    private AudioMixer Mixer() =>
        Entity?.Scene?.AudioOrNull
        ?? throw new InvalidOperationException($"{nameof(AudioSource)} is on no entity in a scene, so {Scene.NoMixerYet}");
}
