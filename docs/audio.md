# Audio

After this page you can play a sound from an entity, play music that survives a scene transition, and
give a settings screen volume sliders.

## Clips and buses

A clip is `assets/audio/<key>.wav` or `.ogg`, authored under the logic project's `Assets/Audio/` and
named in code as `CapsuleAssets.Audio.<Key>` ([`assets.md`](assets.md)). The build measures each
source's duration and reads any loop region out of it, so playback state is derived arithmetically and
not read back from a device.

A bus is a named group voices are mixed and paused through. A game declares its buses at its assembly
root:

```csharp
public static class AudioBuses
{
    /// <summary>The bus that plays the game's music.</summary>
    public static readonly AudioBus Music = new AudioBus("music");

    /// <summary>The bus that plays the game's sound effects.</summary>
    public static readonly AudioBus Sfx = new AudioBus("sfx");
}
```

Every bus is nested under `AudioBus.Master`. Its volume scales every voice, and pausing it pauses every
voice. A bus registers the first time the mixer is asked to change it, at volume 1 and unpaused. A bus's
volume and pause state belong to the run, so both stand across every scene transition.

## A sound from an entity

`AudioSource` is a component that plays one clip and holds the voice, so the entity can stop, pause and
re-level it. The clip is declared as a preload, and the voice is stopped when the entity leaves the
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

`Play()` restarts from the beginning and stops whatever the source was already playing. `Volume`,
`Pitch` and `Pan` apply to the live voice at once and to every later one. `Loop` repeats the clip, or
the clip's loop region where it has one. `PlayOneShot(clip)` plays a separate voice on the same bus and
pan, leaving this source's own voice alone, and returns the `Voice` for the caller to hold. `IsLive`
answers whether a voice is still going, and `IsPlaying` and `IsPaused` split it.

Capsule mixes no position into gain. An `AudioSource` is a handle on a voice, not a point in space.

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

Up to `AudioMixer.MaxVoices` voices sound at once, and a further play steals the oldest. Where every
voice is a live loop, nothing starts. `UnfocusedVolume` is the master scale applied while the window
has no input focus.

## Formats and streaming

`.wav` and `.ogg` are admitted, and MP3 is not. The host holds a `.wav` clip resident for every scene
that uses it. An `.ogg` clip decodes on one background worker as it plays. A looping voice whose clip
carries a loop region streams that way whatever its format, and repeats the region gaplessly.

## In tests

Audio mixing is simulation state. A headless run reaches the same mixer state a windowed one does, so a
test asserts through `Run.Audio` with no playback and no device ([`testing.md`](testing.md)).
