using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Audio;

/// <summary>Plays one clip for its entity and holds the voice.</summary>
/// <remarks>
/// The entity can then stop, pause and re-level it. The clip is declared as a preload, and the
/// voice stops when the entity leaves the scene. A transition therefore silences what a scene's
/// entities were playing, while anything started through <see cref="Run.Audio"/> keeps playing.
/// </remarks>
/// <param name="clip">The clip <see cref="Play()"/> starts.</param>
/// <example>
/// <code>
/// _creak = new AudioSource(CapsuleAssets.Audio.DoorCreakSound) { Bus = AudioBuses.Sfx };
/// Add(_creak);
///
/// // On the step the door starts to open:
/// _creak.Play();
/// </code>
/// </example>
public sealed class AudioSource(AudioClip clip) : Component, IAudioAnchor
{
    // The mixer the live voice belongs to. Kept so the voice is stopped by the mixer that started it,
    // even after the entity has left the scene it reached that mixer through.
    private AudioMixer? _playing;

    private Voice _voice;
    private float _volume = 1f;
    private float _pitch = 1f;
    private float _pan;
    private AudioRange _range;

    // Whether OnStart has run. A later join is a pooled or moved entity's, which AutoPlay replays on.
    private bool _started;

    /// <summary>The clip <see cref="Play()"/> starts and <see cref="Component.CollectAssets"/> declares. Read at each play.</summary>
    public AudioClip Clip { get; set; } = clip;

    /// <summary>The bus this source's voices mix and pause through. <see cref="AudioBus.Master"/> by default, and read at each play.</summary>
    public AudioBus Bus { get; set; }

    /// <summary>
    /// This source's linear amplitude in [0, 1], 1 by default. It applies to the live voice immediately
    /// and to every voice played afterwards.
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Guard.InUnit(value, nameof(value));
            _playing?.SetVolume(_voice, value);
        }
    }

    /// <summary>
    /// Ramps <see cref="Volume"/> from where the live voice is to <paramref name="volume"/> over
    /// <paramref name="seconds"/> on <paramref name="ease"/>. <see cref="Volume"/> reads
    /// <paramref name="volume"/> at once, the target, not the ramp.
    /// </summary>
    /// <remarks>
    /// <see cref="IsFading"/> follows the ramp. Poll it for the landing step. Does nothing to the mixer
    /// when this source owns no voice.
    /// </remarks>
    public void FadeVolume(float volume, float seconds, Ease ease = Ease.Linear)
    {
        Guard.InUnit(volume, nameof(volume));
        Guard.NonNegative(seconds, nameof(seconds));
        Guard.RequireEase(ease, nameof(ease));

        _volume = volume;
        _playing?.FadeVolume(_voice, volume, seconds, ease);
    }

    /// <summary>
    /// The playback-rate multiplier, positive and finite, 1 by default. It applies to the live voice
    /// immediately and to every voice played afterwards.
    /// </summary>
    public float Pitch
    {
        get => _pitch;
        set
        {
            _pitch = Guard.Positive(value, nameof(value));
            _playing?.SetPitch(_voice, value);
        }
    }

    /// <summary>
    /// Where this source's voices sit between the speakers, in [-1, 1], where -1 is hard left, 0 is
    /// centred and 1 is hard right. Centred by default.
    /// </summary>
    /// <remarks>
    /// It applies to the live voice immediately and to every voice played afterwards. A mono clip
    /// pans across the field at equal power. A stereo clip keeps its near side whole and fades its far
    /// side, which is silent at a hard pan. While <see cref="Range"/> is positional the voice ignores this and pans from its entity's
    /// position.
    /// </remarks>
    public float Pan
    {
        get => _pan;
        set
        {
            _pan = Guard.InRange(value, -1f, 1f, nameof(value));
            _playing?.SetPan(_voice, value);
        }
    }

    /// <summary>
    /// How far from the listener this source's voices are heard, following the entity's
    /// <see cref="Entity.WorldPosition"/>. Not positional by default, which plays them at
    /// <see cref="Volume"/> and <see cref="Pan"/> wherever the entity stands.
    /// </summary>
    /// <remarks>It applies to the live voice immediately and to every voice played afterwards.</remarks>
    public AudioRange Range
    {
        get => _range;
        set
        {
            _range = value;
            _playing?.SetRange(_voice, value);
        }
    }

    /// <summary>
    /// The clip time this source's voice has reached on the current step, in seconds from the
    /// clip's start. Reads 0 when the source owns no voice.
    /// </summary>
    /// <remarks>It advances in whole simulation steps.</remarks>
    public double Time => _playing?.GetTime(_voice) ?? 0.0;

    /// <summary>
    /// Whether <see cref="Play()"/> starts a voice that repeats forever. Read at each play.
    /// </summary>
    /// <remarks>
    /// When the clip carries an <see cref="AudioClip.LoopRegion"/>, set by the build from the audio
    /// file or by the game, the voice plays from the clip's beginning to the region's end and then
    /// repeats the region. A clip with no region repeats in full. <see cref="PlayOneShot"/> does
    /// not loop.
    /// </remarks>
    public bool Loop { get; set; }

    /// <summary>Whether the source plays each time its entity joins a scene, a pooled entity's later joins included.</summary>
    public bool AutoPlay { get; set; }

    /// <summary>
    /// Whether this source still owns a voice, either sounding or held by its own pause or its
    /// bus's. It reads false before the first <see cref="Play()"/> and after the voice ends,
    /// whether stopped, expired or stolen.
    /// </summary>
    /// <remarks>
    /// This reports ownership, not audibility, and it is the check to make before starting another
    /// voice. <see cref="IsPlaying"/> and <see cref="IsPaused"/> split it in two.
    /// </remarks>
    public bool IsLive => _playing?.IsLive(_voice) ?? false;

    /// <summary>Whether this source's voice is live and sounding. Reads false while it is held.</summary>
    public bool IsPlaying => _playing?.IsPlaying(_voice) ?? false;

    /// <summary>Whether this source's voice is live and held, by its own pause or its bus's.</summary>
    public bool IsPaused => _playing?.IsPaused(_voice) ?? false;

    /// <summary>
    /// Whether this source's voice is live and still ramping toward the volume <see cref="FadeVolume"/>
    /// or <see cref="Stop(float)"/> gave it. It reads false from the step the ramp lands on.
    /// </summary>
    /// <remarks>
    /// It never reads true after a fade of zero duration or a fade made while the source owned no voice.
    /// Setting <see cref="Volume"/> cancels the ramp, and a later fade replaces it.
    /// </remarks>
    public bool IsFading => _playing?.IsFading(_voice) ?? false;

    /// <summary>
    /// Starts <see cref="Clip"/> from the beginning and stops whatever this source was already playing.
    /// Nothing starts when every voice in the mixer is a live loop.
    /// </summary>
    /// <exception cref="InvalidOperationException">This source is on no entity in a started scene.</exception>
    public void Play() => Play(0.0);

    /// <summary>
    /// Starts <see cref="Clip"/> from <paramref name="startSeconds"/> and stops whatever this source was
    /// already playing. Nothing starts when every voice in the mixer is a live loop.
    /// </summary>
    /// <param name="startSeconds">
    /// The clip time to begin at, in seconds from the clip's start. It must be zero or greater, and below
    /// the clip's duration unless the duration is zero.
    /// </param>
    /// <exception cref="InvalidOperationException">This source is on no entity in a started scene.</exception>
    public void Play(double startSeconds)
    {
        AudioMixer mixer = Mixer();

        _playing?.Stop(_voice);
        _playing = mixer;
        _voice = mixer.Play(new AudioPlayback(Clip)
        {
            Bus = Bus,
            Volume = _volume,
            Pitch = _pitch,
            Pan = _pan,
            Range = _range,
            Position = Entity!.WorldPosition,
            StartSeconds = startSeconds,
            Loop = Loop,
        }, this);
    }

    /// <summary>
    /// Plays <paramref name="clip"/> as a separate one-shot on this source's bus, at its
    /// <see cref="Pan"/> and <see cref="Range"/>, multiplying <see cref="Volume"/> and <see cref="Pitch"/>
    /// by the given scales. This source's own <see cref="Volume"/>, <see cref="Pitch"/> and voice are unchanged.
    /// </summary>
    /// <remarks>
    /// The source does not track the new voice, and the caller holds it. A positional one-shot
    /// stays at the point the entity stood at when it played.
    /// </remarks>
    /// <param name="clip">The clip to play once.</param>
    /// <param name="volumeScale">A factor on <see cref="Volume"/> for this play, itself in [0, 1].</param>
    /// <param name="pitchScale">A factor on <see cref="Pitch"/> for this play, positive and finite.</param>
    /// <returns>The voice started, or <see cref="Voice.None"/> when the mixer had none to give.</returns>
    /// <exception cref="InvalidOperationException">This source is on no entity in a started scene.</exception>
    public Voice PlayOneShot(AudioClip clip, float volumeScale = 1f, float pitchScale = 1f)
    {
        Guard.InUnit(volumeScale, nameof(volumeScale));
        Guard.Positive(pitchScale, nameof(pitchScale));

        return Mixer().Play(new AudioPlayback(clip)
        {
            Bus = Bus,
            Volume = _volume * volumeScale,
            Pitch = _pitch * pitchScale,
            Pan = _pan,
            Range = _range,
            Position = Entity!.WorldPosition,
        });
    }

    /// <summary>Ends this source's voice. Does nothing when the source owns no voice.</summary>
    public void Stop()
    {
        _playing?.Stop(_voice);
        _playing = null;
        _voice = Voice.None;
    }

    /// <summary>
    /// Ramps this source's voice to 0 over <paramref name="seconds"/> and stops it on the landing tick.
    /// Removing the source's entity from its scene during the fade stops the voice at once.
    /// </summary>
    /// <remarks><see cref="IsLive"/> follows the ramp. Poll it for the edge. Does nothing when this source owns no voice.</remarks>
    public void Stop(float seconds)
    {
        Guard.NonNegative(seconds, nameof(seconds));

        _playing?.Stop(_voice, seconds);
    }

    /// <summary>Holds this source's voice where it is. Does nothing when the source owns no voice.</summary>
    public void Pause() => _playing?.Pause(_voice);

    /// <summary>Continues this source's held voice. Does nothing when the source owns no voice.</summary>
    public void Resume() => _playing?.Resume(_voice);

    internal override bool Steps => false;

    // The mixer's settle reads this for a positional voice. The voice stops when the entity leaves the
    // scene, and a detached source is never read.
    Vector2 IAudioAnchor.Position => Entity?.WorldPosition ?? new Vector2(float.NaN);

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (Clip != default)
        {
            assets.Add(Clip);
        }
    }

    /// <summary>Plays a rejoining source that has <see cref="AutoPlay"/> set, as its first join's start did.</summary>
    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        if (_started && AutoPlay)
        {
            Play();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnStart()
    {
        _started = true;
        if (AutoPlay)
        {
            Play();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene() => Stop();

    private AudioMixer Mixer() =>
        Entity?.SceneOrNull?.RunOrNull?.Audio
        ?? throw new InvalidOperationException($"{nameof(AudioSource)} is on no entity in a scene and has no run yet. {Scene.NoRunYet}");

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Clip", Clip.Name + Clip.Extension);
        panel.Field("Bus", Bus.Name);
        panel.Field("Volume", Volume);
        panel.Field("Pitch", Pitch);
        panel.Field("Pan", Pan);
        panel.Field("Time", Time);
        panel.Field("IsPlaying", IsPlaying);
        panel.Field("IsPaused", IsPaused);
        panel.Field("IsFading", IsFading);
        panel.Toggle("Loop", Loop, on => Loop = on);
        panel.Toggle("AutoPlay", AutoPlay, on => AutoPlay = on);
        panel.Command("Play", Play);
        panel.Command("Stop", Stop);
        panel.Command("Pause", Pause);
        panel.Command("Resume", Resume);
    }
}
