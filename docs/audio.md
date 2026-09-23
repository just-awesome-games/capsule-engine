# Audio

After this page you can play a sound from an entity, play music that survives a scene transition, and
give a settings screen volume sliders.

## Clips and buses

A clip is authored under the logic project's `Assets/Audio/` and named in code as
`CapsuleAssets.Audio.<Key>` ([`assets.md`](assets.md#audio)). Playback state is computed from the
duration the build measured and never read back from a device.

A bus is a named group that voices are mixed and paused through. A game declares its buses at its
assembly root:

```csharp
public static class AudioBuses
{
    /// <summary>The bus that plays the game's music.</summary>
    public static readonly AudioBus Music = new AudioBus("music");

    /// <summary>The bus that plays the game's sound effects.</summary>
    public static readonly AudioBus Sfx = new AudioBus("sfx");
}
```

Every bus sits under `AudioBus.Master`, whose volume and pause reach every voice. Bus volume and pause
state belong to the run and stand across every scene transition.

## A sound from an entity

`AudioSource` plays a clip for its entity and holds the voice. The voice stops when the entity leaves the
scene:

```csharp
_footfall = new AudioSource(CapsuleAssets.Audio.StepSoft) { Bus = AudioBuses.Sfx };
Add(_footfall);
```

```csharp
if (_body.IsOnFloor && !wasOnFloor)
{
    _footfall.Play();
}
```

## Music, and everything the run owns

`Run.Audio` is the run's `AudioMixer`. A voice started through it survives a scene transition, and an
`AudioSource`'s voice does not:

```csharp
AudioClip theme = CapsuleAssets.Audio.Theme;
Voice voice = Run.Audio.Play(new AudioPlayback(theme) { Bus = AudioBuses.Music, Loop = true });
```

A settings screen writes to the same mixer:

```csharp
Run.Audio.SetVolume(AudioBuses.Music, volume);
Run.Audio.Pause(AudioBuses.Sfx);
```

The mixer sounds at most `AudioMixer.MaxVoices` voices, shared by the run and every `AudioSource`.
`AudioMixer.Play` documents which voice a further play steals.

## Fades

A fade is a ramp the mixer steps on its own tick. It runs through a pause and past the scene that started
it. A crossfade eases the incoming voice in while the outgoing one eases out, at equal power:

```csharp
Music = Run.Audio.CrossFade(
    menuTheme,
    new AudioPlayback(CapsuleAssets.Audio.Music.Room) { Bus = AudioBuses.Music, Loop = true },
    seconds: 2f);
```

A fade-out lets the old sound clear the mix before the next one starts. A bus fade ducks music under
dialogue:

```csharp
Run.Audio.Stop(Music, seconds: 0.5f);
Run.Audio.FadeVolume(AudioBuses.Music, 0.2f, seconds: 0.3f);
```

A fade raises no completion callback. Poll `IsLive` for the edge.

## Formats and streaming

The host holds a `.wav` clip resident for every scene that uses it. An `.ogg` clip decodes on one background worker as it plays. A looping voice whose clip
carries a loop region streams the same way in either format and repeats the region gaplessly.
