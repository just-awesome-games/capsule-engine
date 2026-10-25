# Audio

After this page you can play a sound from an entity, play music that survives a scene transition, and
give a settings screen volume sliders. Each type's XML docs hold its rules.

Capsule mixes clips on buses and computes every voice's state from the clip's measured duration, in fixed
steps. Which sound answers which event, and where the music handle lives, are the game's. Volumes are
linear amplitude in [0, 1]. A voice given an `AudioRange` is heard from a point in the world, and any
other voice plays at its own `Pan`. Speakers and headphones receive the same stereo mix, with no
headphone processing.

| Need | Piece |
| --- | --- |
| A sound file | An `AudioClip` member of `CapsuleAssets` ([`assets.md`](assets.md#audio)) |
| A group of voices mixed and paused together | `AudioBus`, under `AudioBus.Master` |
| A sound that belongs to an entity | `AudioSource` |
| A sound heard louder the nearer it is | `AudioRange`, on an `AudioSource` or an `AudioPlayback` |
| Music, and any sound that outlives a scene | `Run.Audio`, the run's `AudioMixer` |
| A level or a voice that changes over time | `FadeVolume`, `CrossFade`, and `Stop` with seconds |

## Buses

A game declares its buses at its assembly root:

```csharp
public static class AudioBuses
{
    /// <summary>The bus that plays the game's music.</summary>
    public static readonly AudioBus Music = new AudioBus("music");

    /// <summary>The bus that plays the game's sound effects.</summary>
    public static readonly AudioBus Sfx = new AudioBus("sfx");
}
```

Bus volume and pause state belong to the run and stand across every scene transition. A settings screen
writes them:

```csharp
Run.Audio.SetVolume(AudioBuses.Music, volume);
Run.Audio.Pause(AudioBuses.Sfx);
```

## A sound from an entity

`AudioSource` plays a clip for its entity and holds the voice, which stops when the entity leaves the
scene:

```csharp
_chime = new AudioSource(CapsuleAssets.Audio.ChimeSound) { Bus = AudioBuses.Sfx };
Add(_chime);
```

```csharp
if (context.Input.WasPressed(GameInput.Interact))
{
    _chime.Play();
}
```

## A sound in the world

A voice with a `Range` is heard from a point. An `AudioSource` follows its entity every step:

```csharp
Add(new AudioSource(CapsuleAssets.Audio.HumSound)
{
    Bus = AudioBuses.Sfx,
    Loop = true,
    AutoPlay = true,
    Range = new AudioRange(200f) { Falloff = Ease.InQuad },
});
```

A voice played through `Run.Audio` stays at the `Position` it was given:

```csharp
Run.Audio.Play(new AudioPlayback(CapsuleAssets.Audio.ChimeSound)
{
    Bus = AudioBuses.Sfx,
    Position = Position,
    Range = new AudioRange(240f),
});
```

The listener is the centre of the scene camera's `VisibleRegion`, and a voice falls silent at the range's
`MaxDistance` while keeping its time. A positional voice ignores its own `Pan` and pans by its horizontal
offset from the listener, halfway at the view's edge and hard at a full view width. A sound at the view's
edge is already half a view width from the listener. A `MaxDistance` a little past that distance fades a
sound out as it leaves the screen, and voices whose ranges overlap off screen add up.

## Music

A voice started through `Run.Audio` survives a scene transition. The game keeps its handle on the object
it attaches with `Run.Attach`, which every scene reads as `Run.State<T>()`:

```csharp
_music = Run.Audio.Play(new AudioPlayback(CapsuleAssets.Music.MenuThemeSound) { Bus = AudioBuses.Music, Loop = true });
```

A crossfade eases the incoming voice in while the outgoing one eases out:

```csharp
_music = Run.Audio.CrossFade(
    _music,
    new AudioPlayback(CapsuleAssets.Music.FieldThemeSound) { Bus = AudioBuses.Music, Loop = true },
    seconds: 2f);
```

A fade is a ramp the mixer steps on its own tick, and it runs through a pause and past the scene that
started it. A bus fade ducks music under dialogue, and a fade-out lets the old sound clear the mix:

```csharp
Run.Audio.FadeVolume(AudioBuses.Music, 0.2f, seconds: 0.3f);
Run.Audio.Stop(_music, seconds: 0.5f);
```

A fade raises no completion callback. A game that changes scene once the music has faded polls
`IsFading` on the bus, the voice or the `AudioSource` it started.

## Formats and streaming

A short sound effect is a `.wav`. It ships as lossless FLAC ([`assets.md`](assets.md#audio)), is decoded
when it loads, and is held resident for every scene that uses it. Music and long ambience are `.ogg`,
decoded on a background worker as they play. An `.ogg` of 3 seconds or less is held resident like a
`.wav` and pays no decoder setup per play. A looping voice whose clip carries a loop region repeats
the region gaplessly in either format.
