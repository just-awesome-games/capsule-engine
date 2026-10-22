# Animation

After this page you can play a sprite sheet's clips on an entity, step any value through keys, and ease
any value over time. Each
type's XML docs hold its rules.

Animation is simulation state counted in fixed steps. A clip, a tween and a timer mean the same at any
frame rate, and a headless test can assert on them. Capsule plays frames and values and eases numbers.
Which clip a state shows and what a value drives are the game's.

A duration typed `int` counts fixed steps, 60 a second unless the shell calls `EngineBuilder.WithFixedStep`,
and a duration typed `float` counts seconds. `Countdown.Start(30)` is half a second at the default rate.

| Need | Piece |
| --- | --- |
| Frames cut from a texture, and the clips played over them | A `*.sheet.json` sprite sheet |
| A clip playing on a sprite | `SpriteAnimator` on a `SpriteRenderer` |
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
| Scripted beats that wait between them | `Sequence`, yielding `Wait` values |

## Sprite sheets

A sheet names one texture, the frames it cuts from it, and the clips played over those frames. The build
compiles `Sprites/door.sheet.json` into `CapsuleAssets.Sprites.DoorSheet`, with `Frames.Shut` a `Sprite`,
`Clips.Opening` a `SpriteClip` and `Events.Opened` an event's name. A misspelt frame is a build error, and
no sheet ships.

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
    { "name": "opening", "frames": [ { "frame": "shut", "ticks": 6 }, { "frame": "open", "ticks": 1, "events": ["opened"] } ] }
  ]
}
```

A sheet may also declare `sockets` and `boxes`, named points and rects each frame sets. A socket or a box
belongs to the drawing. An event belongs to the clip entry, and the same frame played by another clip
raises none of it. Pivots, socket points and boxes are texels from the frame's top-left corner, and
`ticks` are fixed steps. A clip plays once and holds its last frame unless its entry sets `"loop": true`.
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
frame the door opens.

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
Position = Vector2.Lerp(_hiddenAt, _shownAt, _slide.Value);
```

A `Sequence` runs an iterator's beats, waiting between them, and its owner steps it the same way:

```csharp
_showDoor.Start(ShowDoor(avatar));

private IEnumerable<Wait> ShowDoor(Entity followed)
{
    Scene.Camera.Follow(_door);
    yield return Wait.Ticks(40);
    _door.Opening = true;
    yield return Wait.Until(() => _door.IsOpen);
    Scene.Camera.Follow(followed);
}
```
