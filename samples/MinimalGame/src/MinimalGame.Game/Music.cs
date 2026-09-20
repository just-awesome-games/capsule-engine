using Capsule.Audio;

namespace MinimalGame.Game;

/// <summary>The one music voice, kept on the run so every scene declares its track and none holds a voice.</summary>
public sealed class Music(AudioMixer mixer)
{
    private Voice _voice;
    private AudioClip _clip;

    /// <summary>The voice the current track plays on, or <see cref="Voice.None"/> before the first <see cref="Play"/>.</summary>
    public Voice Voice => _voice;

    /// <summary>Whether <paramref name="clip"/> is the track playing now.</summary>
    public bool IsPlaying(AudioClip clip) => _clip == clip && mixer.IsLive(_voice);

    /// <summary>Crossfades to <paramref name="clip"/>. A track already playing is left alone.</summary>
    public void Play(AudioClip clip, float seconds = 2f)
    {
        if (IsPlaying(clip))
        {
            return;
        }

        // After Stop the old voice is still fading on its own tail; the new track fades in beside it.
        Voice from = _clip == default ? Voice.None : _voice;
        _voice = mixer.CrossFade(from, new AudioPlayback(clip) { Bus = AudioBuses.Music, Loop = true }, seconds);
        _clip = clip;
    }

    /// <summary>Fades the track out and forgets it.</summary>
    public void Stop(float seconds)
    {
        mixer.Stop(_voice, seconds);
        _clip = default;
    }
}
