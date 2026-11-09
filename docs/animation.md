# Animation

After this page you can play a sprite sheet's clips on an entity, step any value through keys, and ease
any value over time. Each
type's XML docs hold its rules.

Animation is simulation state counted in fixed steps. A clip, a tween and a timer mean the same at any
frame rate, and a headless test can assert on them. Capsule plays frames and values and eases numbers.
Which clip a state shows and what a value drives are the game's.

A duration typed `int` counts fixed steps and one typed `float` counts seconds
([determinism contract](architecture.md#determinism-contract)). `Countdown.Start(30)` is half a second at
the default rate.

| Need | Piece |
| --- | --- |
| Frames cut from a texture, and the clips played over them | A `*.sheet.json` sprite sheet |
| A clip playing on a sprite | `SpriteAnimator` on a `SpriteRenderer` |
| One clip, then another when it ends | `SpriteAnimator.Play(clip, then: next)` |
| A point or a collider the drawn frame places | `SpriteRenderer.Socket` and `SpriteRenderer.Box` |
| Another drawing of a sheet's frames, such as a hat | A `"variantOf"` sheet's `Variant`, set on `SpriteRenderer.Variant` |
| Layers showing the frame an animator plays | `new SpriteAnimator(avatar, hat)` |
| A moment in a clip, such as a sound cue | The sheet's `events`, read with `SpriteAnimator.Reached` |
| Any value stepped through keys, such as a palette row or a flag | `ValueClip<T>` played by `ValueAnimator<T>` |
| Keys blended linearly | `ValueClip.Linear`, for `float`, `Vector2`, `Vector4` and `ColorRgba` |
| Keys authored as data | A `*.clip.json` value clip |
| A value eased from 0 to 1 over steps | `Tween`, with an `Ease` and a `TweenLoop` |
| An ease applied to a progress of your own | `Easing.Apply` |
| A value or a colour over a normalised time | `Curve`, `Gradient` |
| A delay or a cooldown with no value to read | `Countdown` |
| Scripted beats that wait between them | A `Sequence` stepping a `Routine` |

## Sprite sheets

A sheet names one texture, the frames it cuts from it, and the clips played over those frames. The build
compiles `Sprites/door.sheet.json` into `CapsuleAssets.Sprites.DoorSheet`, with `Frames.Shut` a `Sprite`,
`Clips.Opening` a `SpriteClip`, `Events.Opened` an event's name, and `Sockets` and `Boxes` classes of
names. A misspelt frame or socket is a build error, and no sheet ships.

```json
{
  "formatVersion": 1,
  "texture": "textures/door.png",
  "events": [ { "name": "opened" } ],
  "frames": [
    { "name": "shut", "x": 0, "y": 0, "width": 16, "height": 32, "pivot": [8, 32] },
    { "name": "open", "x": 16, "y": 0, "width": 16, "height": 32, "pivot": [8, 32] }
  ],
  "clips": [
    { "name": "opening", "frames": [ { "frame": "shut", "ticks": 6 }, { "frame": "open", "ticks": 1, "events": ["opened"] } ] },
    { "name": "open", "loop": true, "frames": [ { "frame": "open", "ticks": 1 } ] }
  ]
}
```

A sheet may also declare `sockets` and `boxes`, named points and rects each frame sets. A socket or a box
belongs to the drawing. An event belongs to the clip entry, and the same frame played by another clip
raises none of it. Pivots, socket points and boxes are texels from the frame's top-left corner, and
`ticks` are fixed steps. A clip plays once and holds its last frame unless its entry sets `"loop": true`.

A frame that is another's mirror image cuts the same region and sets `"flipX": true` or `"flipY": true`, and
needs no texels of its own. Its pivot, sockets and boxes are measured on the frame as it draws, and a
renderer's `FlipX` or `FlipY` mirrors it again:

```json
{ "name": "left", "x": 0, "y": 0, "width": 16, "height": 16, "pivot": [8, 16] },
{ "name": "right", "x": 0, "y": 0, "width": 16, "height": 16, "pivot": [8, 16], "flipX": true }
```

The format's JSON Schema documents every field ([Editor completion](configuring-assets.md#editor-completion)).

## Playing a clip

```csharp
SpriteRenderer sprite = new(CapsuleAssets.Sprites.DoorSheet.Frames.Shut);
Add(sprite);
_animator = new SpriteAnimator(sprite);
Add(_animator);
_animator.Play(CapsuleAssets.Sprites.DoorSheet.Clips.Opening);
```

A step reads `_animator.Reached(CapsuleAssets.Sprites.DoorSheet.Events.Opened)` to play its sound on the
frame the door opens. A play can name the clip that follows, and the animator swaps to it on the step the first
finishes:

```csharp
_animator.Play(CapsuleAssets.Sprites.DoorSheet.Clips.Opening, then: CapsuleAssets.Sprites.DoorSheet.Clips.Open);
```

## Variants and layers

A sheet naming its base sheet in `"variantOf"` redraws each base frame from its own texture, and a frame
giving only its name draws nothing. A renderer draws its `Variant` while sockets and boxes stay the held
frame's. An animator writes every layer it is given:

```csharp
SpriteRenderer hat = new(avatar.Sprite) { Variant = CapsuleAssets.Sprites.AvatarHatSheet.Variant };
Add(hat);
_animator = new SpriteAnimator(avatar, hat);
```

## Value clips

A `ValueClip<T>` is a run of keys, each a value held for whole ticks, and a `ValueAnimator<T>` plays one by
handing each value to the setter it was built with. The setter runs on `Play` and then whenever the
value changes. `Play`, `IsFinished`, `Paused` and `Speed` work as they do on `SpriteAnimator`:

```csharp
_glow = new ValueAnimator<float>(row => _material.Set("Row", row));
Add(_glow);
_glow.Play(new ValueClip<float>([(1f, 8), (2f, 8), (3f, 7)]));
```

A clip built with `ValueClip.Linear` moves from each key's value toward the next across that key's ticks,
and its last key holds unless the clip loops back to the first. A type that does not blend has no
`Linear`. A `*.clip.json` holds numbers, and the build compiles `Clips/pulse.clip.json` into
`CapsuleAssets.Clips.PulseClip`, a `ValueClip<float>`:

```json
{ "formatVersion": 1, "loop": true, "blend": "linear", "keys": [ { "value": 0, "ticks": 30 }, { "value": 1, "ticks": 30 } ] }
```

`"blend"` is `"step"` unless the file sets `"linear"`, and `"loop"` is false unless set.

## Tweens and timers

Nothing raises a completion signal for a `Tween`, a `Countdown` or a `Sequence`. The owner that steps one
reads `JustFinished`, `IsFinished` or `IsRunning` in its step:

```csharp
_slide.Start(20, Ease.OutCubic);
```

```csharp
_slide.Step();
Position = DeterministicMath.Lerp(_hiddenAt, _shownAt, _slide.Value);
```

A `Sequence` runs a routine, and its owner steps it the same way. A routine is an `async` method that returns
`Routine` and awaits `Wait` values:

```csharp
_showDoor.Start(ShowDoor(avatar));

private async Routine ShowDoor(Entity followed)
{
    Scene.Camera.Follow(_door);
    await Wait.Ticks(40);
    _door.Opening = true;
    await Wait.Until(_door, static door => door.IsOpen);
    Scene.Camera.Follow(followed);
}
```

A routine can await another: `await Walk(north);` runs `Walk` to its end before the next line. It awaits nothing
else. A `Task` would resume outside the step, and `CAP103` refuses it.

A routine has no `StepContext`. A beat that moves its entity steps it each tick by `Scene.StepSeconds`, the length of
the step running now.

An optimized build pools routine calls by method. A method's pool grows only when more of its calls are live than
ever before, and a call goes back when its routine ends or its sequence stops. An entity stops its sequence when it
leaves the scene. A pooled entity then starts its behaviour as a routine on every spawn and allocates nothing. A
debug build allocates on each call. A `Wait.Until` given its state and a `static` lambda, as above, allocates nothing. A lambda that captures anything allocates each time its `await` runs.

`Stop` abandons the routine where it stands. Its `finally` blocks do not run, and the owner undoes what it set.
