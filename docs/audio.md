# Audio

After this page you can play a sound from an entity, play music that survives a scene transition, give
a settings screen volume sliders, and echo or muffle a bus. Each type's XML docs hold its rules.

Capsule mixes clips on buses and computes every voice's state from the clip's measured duration, in fixed
steps. Which sound answers which event, and where the music handle lives, are the game's. Volumes are
linear amplitude in [0, 1]. A voice given an `AudioRange` is heard from a point in the world, and any
other voice plays at its own `Pan`. Speakers and headphones receive the same stereo mix, with no
headphone processing.

| Need | Piece |
| --- | --- |
| A sound file | An `AudioClip` member of `CapsuleAssets` ([`assets.md`](assets.md#audio)) |
| A group of voices mixed and paused together | `AudioBus`, under `AudioBus.Master` or a parent bus |
| A sound that belongs to an entity | `AudioSource` |
| A sound heard louder the nearer it is | `AudioRange`, on an `AudioSource` or an `AudioPlayback` |
| Hearing positional sound from an entity | `Camera.Listener` |
| Music, and any sound that outlives a scene | `Run.Audio`, the run's `AudioMixer` |
| A level or a voice that changes over time | `FadeVolume`, `CrossFade`, and `Stop` with seconds |
| An echo, or a bus muffled or thinned | `AudioEffects` and `AudioEcho`, through `SetEffects` and `FadeEffects` |
| Sound heard sooner after its step, or no gaps on a slow machine | `EngineBuilder.WithAudioLatency` |

## Buses

A game declares its buses at its assembly root:

```csharp
public static class AudioBuses
{
    /// <summary>The bus the player's music volume is set on.</summary>
    public static readonly AudioBus Music = new AudioBus("music");

    /// <summary>The bus the music plays on, under <see cref="Music"/>.</summary>
    public static readonly AudioBus Score = new AudioBus("score", Music);

    /// <summary>The bus that plays the game's sound effects.</summary>
    public static readonly AudioBus Sfx = new AudioBus("sfx");
}
```

Bus volume, effects and pause state belong to the run and stand across every scene transition. A
settings screen writes them:

```csharp
Run.Audio.SetVolume(AudioBuses.Music, volume);
Run.Audio.Pause(AudioBuses.Sfx);
```

A bus adds into the master unless it is declared under a parent. A child runs its own effects and volume,
then adds into its parent. The two volumes multiply. Pausing the parent holds every voice under it. A
parent is declared above its children, since a static field below is still null as the child is made.

Each owner of a level gets its own bus. A settings slider sets the parent's volume, and the game fades
the child's for a moment such as a pause. Neither overwrites the other.

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

The listener is the scene camera's `Listener` while that entity is in the scene, and otherwise the centre
of the camera's `VisibleRegion`. A voice falls silent at the range's `MaxDistance` while keeping its time.
A positional voice ignores its own `Pan` and pans by its horizontal offset from the listener, as
`AudioPlayback.Position` documents. Heard from the view's centre, a sound at the view's edge is half
a view width away, and a `MaxDistance` a little past that distance fades it out as it leaves the screen.
Voices whose ranges overlap off screen add up.

Heard from the entity the player steers, a sound keeps its side of that entity while the view leads it or
stops at its bounds:

```csharp
Scene.Camera.Listener = avatar;
```

## Music

A voice started through `Run.Audio` survives a scene transition. The game keeps its handle on its
`GameInstance`, which every scene reads as `Run.State<T>()`:

```csharp
_music = Run.Audio.Play(new AudioPlayback(CapsuleAssets.Music.MenuThemeSound) { Bus = AudioBuses.Score, Loop = true });
```

A crossfade eases the incoming voice in while the outgoing one eases out:

```csharp
_music = Run.Audio.CrossFade(
    _music,
    new AudioPlayback(CapsuleAssets.Music.FieldThemeSound) { Bus = AudioBuses.Score, Loop = true },
    seconds: 2f);
```

A fade is a ramp the mixer steps on its own tick, and it runs through a pause and past the scene that
started it. A fade on a child bus ducks music under dialogue while its parent keeps the player's
volume, and a fade-out lets the old sound clear the mix:

```csharp
Run.Audio.FadeVolume(AudioBuses.Score, 0.2f, seconds: 0.3f);
Run.Audio.Stop(_music, seconds: 0.5f);
```

A fade raises no completion callback. A game that changes scene once the music has faded polls
`IsFading` on the bus, the voice or the `AudioSource` it started.

## Effects

A bus can echo its sound and filter it. Effects belong to a bus, never to one voice. Sounds that share
a space share a bus:

```csharp
Run.Audio.SetEffects(AudioBuses.Sfx, new AudioEffects
{
    Echo = new AudioEcho(0.25f) { Feedback = 0.4f, HighCutHertz = 6000f },
});
```

`AudioEffects` states the order a bus runs its effects in. A fade built from `GetEffects` changes one
setting and keeps the rest:

```csharp
Run.Audio.FadeEffects(AudioBuses.Score, Run.Audio.GetEffects(AudioBuses.Score) with { LowPassHertz = 900f }, 0.2f);
```

Setting a bus's effects to what they already are changes nothing, and a scene may set its space on
every load.

## Formats and streaming

A short sound effect is a `.wav`. It ships as lossless FLAC ([`assets.md`](assets.md#audio)), is decoded
when it loads, and is held resident for every scene that uses it. Music and long ambience are `.ogg`,
decoded on a background worker as they play. An `.ogg` of 3 seconds or less is held resident like a
`.wav` and pays no decoder setup per play. A looping voice whose clip carries a loop region repeats
the region gaplessly in either format.
