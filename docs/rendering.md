# Rendering

After this page you can put sprites, rects and text on screen, frame them with a camera, and control
what draws over what. Each type's XML docs hold its rules.

An entity looks like the renderer components it holds. A camera places the world layer, and the screen
layer draws over it in canvas pixels. Capsule draws sprites, flat rects, text, particles, tile maps and
lights. What a game's world looks like is the game's.

| Need | Piece |
| --- | --- |
| A fixed pixel resolution | `EngineBuilder.WithRenderResolution` and `WithSampling` |
| What part of the world shows | `Camera` |
| Hiding, fading or flashing | `Visible`, `Tint` and `Flash` on `Entity`, `Visible` and `Color` on a renderer |
| What draws over what | `ZIndex`, `Scene.YSort`, and `ScreenEntity` for the screen layer |
| A layer that scrolls slower than the world | `Entity.ScrollFactor` |
| Light in a dark scene | `Scene.Ambient` and `PointLight` |
| A reaction to coming on screen | `VisibleOnScreenNotifier2D` |

Animation is [`animation.md`](animation.md), your own shader is [`shaders.md`](shaders.md), and
menus and panels are [`ui.md`](ui.md).

## The surface, the canvas and the camera

| Span | Set by | What it is |
| --- | --- | --- |
| Render surface | `EngineBuilder.WithRenderResolution(width, height)` | A fixed pixel surface, letterboxed into the window. Absent, the frame is drawn at window size. |
| Canvas | `EngineBuilder.WithCanvas(width, height)`, or `Run.Canvas` during the run | The screen layer's extent in canvas pixels. Defaults to the render surface, then to the window size. |
| Viewport | `Camera.ViewportSize` | World units the camera spans. A sprite draws one texel per world unit. `Camera.Fit` answers an output of another aspect ratio. |

A pixel-art game declares one render surface and point sampling, and lets the canvas follow the surface.
The interface is then drawn in the same pixels as the world:

```csharp
return CapsuleBoot.Configure("My Game", new DesktopPlatform())
    .WithCommandLine(args)
    .WithRenderResolution(320, 180)
    .WithSampling(TextureSampling.Point)
    .RunScene<PlayField>();
```

## The camera

A scene sets its camera in its constructor, or a document's `camera` object installs the camera its
`type` names. A camera follows a subject inside bounds at a zoom:

```csharp
public PlayField()
{
    Avatar avatar = new(new Vector2(160f, 90f));
    Add(avatar);

    Camera = new Camera { ViewportSize = new Vector2(320f, 180f), Zoom = 2f, Bounds = new Rect(Vector2.Zero, new Vector2(960f, 540f)) };
    Camera.Follow(avatar);
}
```

| Need | Member |
| --- | --- |
| Slack before the camera moves, or an aim off the subject | `Deadzone`, `FollowOffset` |
| A change of bounds that glides | `SetBounds` with a `BoundsTransition` |
| A push the game owns, such as a recoil | `Offset` |
| A shake, or one shaped per call | `Shake`, with a `CameraShake` |
| A cut with no glide | `Teleport` |

## Renderers

Each renderer carries an `Offset` from the entity's position, a `Color`, and a `ZIndex` of its own within
the entity's band. A sprite's pivot is its region's top-left texel unless one is given. Pass the centre for
a sprite that turns or flips in place.

| Renderer | Draws |
| --- | --- |
| `SpriteRenderer` | One `Sprite`, or that sprite tiled across `Tiling`. |
| `ColorRect` | A flat rect of `Size`. |
| `Label` | A run of a `BitmapFont`, wrapped and aligned inside `Size`. |
| `NineSlice` | A sprite stretched to `Size` with its corners kept. |
| `ParticleEmitter` | A fixed pool of sprite particles stepped on the fixed tick. |

A game that needs geometry no renderer draws subclasses `Renderer` and writes into the `FrameView` it is
handed. When `Draw` runs is in [`architecture.md`](architecture.md#simulation-and-host).

## Hiding, fading and flashing

`Entity.Visible`, `Entity.Tint` and `Entity.Flash` reach everything beneath the entity.
`Renderer.Visible` and a renderer's own `Color` reach one renderer. None of them stops the entity
stepping:

```csharp
Visible = _shown;
Tint = ColorRgba.White with { A = (byte)(255f * (1f - _fade.Value)) };
Flash = _selected ? 0.5f : 0f;
```

## Two layers, and draw order

A frame carries two ordered lists. The world layer is placed by the camera and culled against it. The
screen layer is in canvas pixels, culled against `Run.Canvas`, and draws over the world layer. A
`ScreenEntity` and every renderer it holds are on the screen layer ([`ui.md`](ui.md)), and anything
else is in the world.

Within a layer, what draws later has the higher sum of the entity's `ZIndex` up its ancestry and the
renderer's own `ZIndex`. Ties break by file order in a document and then by attachment order. A top-down
scene sets `Scene.YSort` to order world renderers in one band by their root entity's Y.

## Parallax

An entity's `ScrollFactor` scales how far it moves with the camera. Zero on both axes pins the entity to
the screen, and one is the world. Draw order is the same `ZIndex` sum whatever the factor:

```csharp
ScrollFactor = new Vector2(0.5f, 1f);
ZIndex = -10;
Add(new SpriteRenderer(Hills) { Tiling = new Vector2(float.PositiveInfinity, 0f) });
```

A document may author the factor instead, as `scrollFactor` on an entry. A component that answers at the
authored position, such as a collider or a `VisibleOnScreenNotifier2D`, refuses a factor other than one.

## Particles

A `ParticleEmitter` spawns from its fixed pool at `Rate` per second, or in a burst from `Emit(count, at)`,
and steps every particle on the fixed tick. `Lifetime`, `Speed` and the other ranges take one value or a
`(min, max)` pair drawn per particle. `Color` and `ScaleOverLifetime` read a `Gradient` and a `Curve` over
each particle's life:

```csharp
Add(new ParticleEmitter(Sprite.White, capacity: 32)
{
    Rate = 20f,
    Lifetime = (0.4f, 0.6f),
    Direction = -Vector2.UnitY,
    Spread = 40f,
    Speed = (30f, 50f),
    Color = Gradient.Linear(ColorRgba.White, ColorRgba.White with { A = 0 }),
});
```

An emitter built from a sheet's clip plays it on every particle, each frame for its authored steps, and a
clip that does not loop ends its particle. `FrameMode` stretches the clip over each particle's life instead,
or picks one frame at spawn. `FlipX` mirrors each new particle as a negative X scale on the entity would,
and a particle keeps that mirror for its life:

```csharp
_puff = new ParticleEmitter(CapsuleAssets.Sprites.PuffSheet.Clips.Puff, capacity: 4) { Direction = -Vector2.UnitX, Speed = 12f };
...
_puff.FlipX = facingLeft;
_puff.Emit(1);
```

## Lighting

A scene lowers the light with `Scene.Ambient`. A `PointLight` draws into the frame's light map, which the
host multiplies over the world layer. A world sprite drawn with `BlendMode.Additive` in a lit frame lights
the map too, and a glow stays bright in a dim room. A scene with white ambient and no light runs no pass.

## Text

A `Label` draws text from a bitmap font baked to texture pages. A run is laid out left to right, one
glyph per codepoint, with the kerning the font declares. There is no shaping, no bidirectional layout and
no distance field.

`BitmapFont.Default` ships inside the runtime and needs no asset. Other fonts are authored under `Assets/`
([`assets.md`](assets.md#fonts)). `GlyphRun` is the layout every placement comes from, for code that
emits its own per-glyph sprites.

## Visibility

A `VisibleOnScreenNotifier2D` reports when a rect on its entity meets the camera's `VisibleRegion`. Its
rect follows scale up the ancestry as a `BoxCollider2D` does. An entity with a scale of `(-1, 1)` mirrors
its sprite, its box and its notifier together.
