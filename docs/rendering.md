# Rendering

After this page you can put sprites, rects and text on screen, frame them with a camera that follows
the player, and control what draws over what.

## The surface, the canvas and the camera

Three spans decide what a player sees, and each is set in one place.

| Span | Set by | What it is |
| --- | --- | --- |
| Render surface | `EngineBuilder.WithRenderResolution(width, height)` | A fixed pixel surface, letterboxed into the window. Absent, the frame is drawn at window size. |
| Canvas | `EngineBuilder.WithCanvas(width, height)`, or `Run.Canvas` during the run | The screen layer's extent in canvas pixels. Defaults to the render surface, then to the window size. |
| Viewport | `Camera.ViewportSize` | World units the camera spans. Zero draws nothing. |

Pixel-art games declare one render surface and let the canvas follow it, so the interface is drawn in
the same pixels as the world:

```csharp
return CapsuleBoot.Configure("Minimal Game", new DesktopPlatform())
    .WithCommandLine(args)
    .WithRunStart(GameBoot.Start)
    .WithRenderResolution(320, 180)
    .WithSampling(TextureSampling.Point)
    .RunScene<MainMenu>();
```

`Camera.Fit` decides what happens on an output whose aspect ratio differs from the viewport.
`Letterbox` shows that span and turns the slack into bars, `Expand` reveals more world on the slack
axis at the same scale, and `FixedHeight` keeps the vertical span and follows the output's aspect
across. On a declared render surface the world is drawn at the declared pixels per unit under either
sampling. Point sampling snaps each sprite to the surface's pixel grid and presents the surface at an
integer scale when the output can hold it.

## A camera that follows

A scene installs its camera and touches it no further. A camera subclass has two hooks, `OnStart` to
find what it frames and `OnLateStep` to settle the framing once every entity has moved:

```csharp
public sealed class GameCamera : Camera
{
    private Player _subject = null!;

    public GameCamera() => ViewportSize = World.ViewportSize;

    protected override void OnStart()
    {
        _subject = Scene.FindSingle<Player>();

        // The room opens framed on the player, with no sweep from the world origin.
        Teleport(_subject.Position);
    }

    protected override void OnLateStep(in StepContext context) => Center = _subject.Position;
}
```

`Center` is the framing target and is interpolated between steps. `Teleport` moves the camera with no
interpolation, for the frame a scene opens on and for a hard cut. `Bounds` confines the visible region
to a world rect without moving `Center`. `VisibleRegion` is the world rect the last frame drew, settled
once a step right after `OnLateStep`.

## Renderers

What an entity looks like is one or more renderer components on it. Each carries an `Offset` from the
entity's position, a `Color`, and a `ZIndex` of its own within the entity's band.

| Renderer | Draws |
| --- | --- |
| `SpriteRenderer` | One `Sprite`, with `FlipX`, `FlipY` and `Tiling`. `Socket(name)` returns a child entity the drawn frame places. |
| `ColorRect` | A flat rect of `Size`. |
| `Label` | A run of a `BitmapFont`, wrapped and aligned inside `Size`. |
| `NineSlice` | A sprite stretched to `Size` with its `Insets` corners kept. |

```csharp
SpriteRenderer sprite = new(CapsuleAssets.Sprites.Actors.Player.Frames.Idle0);
Add(sprite);
Muzzle = sprite.Socket(CapsuleAssets.Sprites.Actors.Player.Sockets.Muzzle);

_animator = new SpriteAnimator(sprite);
Add(_animator);
```

A `SpriteAnimator` plays a clip on a `SpriteRenderer`. Clips are held in whole fixed steps, so
animation is simulation state and means the same at any frame rate. `Play` ignores a clip that is
already playing, so a step may ask for the clip the state implies without restarting it:

```csharp
_animator.Play(velocity.X != 0f ? CapsuleAssets.Sprites.Actors.Player.Clips.Walk : CapsuleAssets.Sprites.Actors.Player.Clips.Idle);
```

A `Tween` is the eased value beside all this. It counts a duration in whole fixed steps, reads through
an easing curve, and drives a flash, a slide or a one-off eased value. A `Countdown` is the same timer
with no value to read, for a cooldown, a delay or a lifetime.

A game that needs geometry no renderer draws subclasses `Renderer` and writes into the `FrameView` it
is handed. The sheet format, atlases and where sprites come from are [`assets.md`](assets.md).

## Two layers, and draw order

A frame carries two ordered lists. The world layer is placed by the camera and culled against it. The
screen layer is in canvas pixels, culled against `Run.Canvas`, and draws over the world layer. An
entity's type decides its layer: a `ScreenEntity` is on the screen layer, anything else is in the
world, and every renderer it holds follows.

Within a layer, what draws later has the higher sum of the entity's `ZIndex` up its ancestry and the
renderer's own `ZIndex`. Ties break by file order in a document and then by attachment order.

A `ScreenEntity` is placed by an `Anchor`, a fraction of the canvas on each axis, plus an offset in
canvas pixels, so an element keeps its distance from the edge it was anchored to whatever the canvas
is:

```csharp
private readonly HealthBar _healthBar = new(Anchor.TopLeft, new Vector2(8f, 8f));
```

Menus are `Focusable` components under one `FocusNavigator`. The navigator owns which item has focus
and moves it from the game's own focus actions, pointer included. A focus change applies at once, so a
handler sees the new focus. Changing the navigator's items or its focus from inside one of its own
focus events throws.

## Parallax

An entity may carry a `ScrollFactor`, and the host draws it by a virtual camera whose corner is moved
by that factor about `Camera.ScrollOrigin`. Zero on both axes pins the entity to the screen, and one
is the world. Draw order is the same `ZIndex` sum whatever the factor:

```csharp
public Sky(EntitySpawn spawn)
    : base(spawn)
{
    ScrollFactor = Vector2.Zero;
    ZIndex = -20;
    Add(new SpriteRenderer(Dusk) { Tiling = new Vector2(float.PositiveInfinity, 0f) });
}
```

A document may author the factor instead, as `scrollFactor` on an entry ([`scenes.md`](scenes.md)). A
`Tiling` of positive infinity on an axis repeats the frame without bound along it.

## Text

Capsule draws text from a bitmap font: a font baked to texture pages with a glyph rectangle per
codepoint. Nothing is rasterized at run time and no outline font is opened. A run is laid out left to
right, one glyph per codepoint, with the kerning the font declares. There is no shaping, no
bidirectional layout and no distance field.

```csharp
Add(new Label(CapsuleAssets.Fonts.Menu, "Minimal Game")
{
    Pivot = Pivot.Top,
    HorizontalAlignment = HorizontalAlignment.Center,
});
```

`Label` wraps inside `Size` by `Wrap`, aligns by `HorizontalAlignment` and `VerticalAlignment`, and
reveals a prefix through `VisibleCharacters` for a typewriter effect. `SetText(ReadOnlySpan<char>)`
writes a run without allocating a string. `BitmapFont.Default` ships inside the runtime and needs no
asset. `GlyphRun` is the layout pass every placement comes from, so code emitting its own per-glyph
sprites enumerates the geometry the engine draws and measures. Fonts are authored under
`Assets/Fonts/` ([`assets.md`](assets.md#fonts)).

## Visibility

A `VisibleOnScreenNotifier2D` watches a rect on its entity against the camera's visible region and
raises `ScreenEntered` and `ScreenExited`. It is simulation only, so a headless run answers as a
windowed one does. Every notifier settles once a step, after the step's deferred adds have landed and
against the region that step's frame was framed with, so the events describe that frame from the
entity's first step. One registered from inside another notifier's handler first settles on the next
step.
