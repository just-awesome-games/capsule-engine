# Animation

After this page you can play a sprite sheet's clips on an entity and ease any value over time. Each
type's XML docs hold its rules.

Animation is simulation state counted in fixed steps. A clip, a tween and a timer mean the same at any
frame rate, and a headless test can assert on them. Capsule plays frames and eases numbers. Which clip a
state shows and what a value drives are the game's.

A duration typed `int` counts fixed steps, 60 a second unless the shell calls `EngineBuilder.WithFixedStep`,
and a duration typed `float` counts seconds. `Countdown.Start(30)` is half a second at the default rate.

| Need | Piece |
| --- | --- |
| Frames cut from a texture, and the clips played over them | A `*.sheet.json` sprite sheet |
| A clip playing on a sprite | `SpriteAnimator` on a `SpriteRenderer` |
| A point or a collider the drawn frame places | `SpriteRenderer.Socket` and `SpriteRenderer.Box` |
| A moment in a clip, such as a sound cue | The sheet's `events`, read with `SpriteAnimator.Reached` |
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
