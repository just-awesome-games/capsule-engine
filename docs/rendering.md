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

A pixel-art game declares one render surface and point sampling, and lets the canvas follow the surface.
The interface is then drawn in the same pixels as the world:

```csharp
return CapsuleBoot.Configure("Minimal Game", new DesktopPlatform())
    .WithCommandLine(args)
    .WithRunStart(GameBoot.Start)
    .WithRenderResolution(320, 180)
    .WithSampling(TextureSampling.Point)
    .RunScene<MainMenu>();
```

`Camera.Fit` decides what an output whose aspect ratio differs from the viewport shows. Point sampling
snaps each sprite to the surface's pixel grid. An unturned sprite, or one turned by whole quarter turns,
snaps all four edges. Abutting tiles then share their edges at any zoom. A declared render surface
fills the output edge to edge on its binding axis, at whatever fractional scale keeps its aspect, with
bars on the other axis. A point-sampled surface enlarged past a whole scale is first enlarged to the
next whole scale with the nearest texel and then filtered down to fit. Its texels stay crisp and even in width. A window smaller
than the surface filters it straight down.

## A camera that follows

A document's `camera` key installs a camera, or a scene's constructor sets one directly. A subclass sets
its feel once and names its subject in `OnStart`:

```csharp
public sealed class GameCamera : Camera
{
    public GameCamera()
    {
        ViewportSize = World.ViewportSize;
        Deadzone = new Vector2(16f, 96f);
        SmoothTime = 0.25f;
    }

    protected override void OnStart()
    {
        if (Scene.Size.X > 0f && Scene.Size.Y > 0f)
        {
            Bounds = new Rect(Vector2.Zero, Scene.Size);
        }

        Follow(Scene.FindSingle<Player>());
    }
}
```

`Camera` documents how each framing lever composes with the follow.

## Renderers

What an entity looks like is one or more renderer components on it. Each carries an `Offset` from the
entity's position, a `Color`, and a `ZIndex` of its own within the entity's band.

| Renderer | Draws |
| --- | --- |
| `SpriteRenderer` | One `Sprite`. `Socket(name)` returns a child entity the drawn frame places. |
| `ColorRect` | A flat rect of `Size`. |
| `Label` | A run of a `BitmapFont`, wrapped and aligned inside `Size`. |
| `NineSlice` | A sprite stretched to `Size` with its `Insets` corners kept. |
| `ParticleEmitter` | A fixed pool of sprite particles stepped on the fixed tick. |

A `SpriteAnimator` plays a sheet's clips on a `SpriteRenderer`. A step may ask for the clip its state
implies every step, because `Play` does not restart a clip already playing:

```csharp
SpriteRenderer sprite = new(CapsuleAssets.Sprites.Actors.PlayerSheet.Frames.Idle0);
Add(sprite);
Muzzle = sprite.Socket(CapsuleAssets.Sprites.Actors.PlayerSheet.Sockets.Muzzle);

_animator = new SpriteAnimator(sprite);
Add(_animator);
```

```csharp
_animator.Play(velocity.X != 0f ? CapsuleAssets.Sprites.Actors.PlayerSheet.Clips.Walk : CapsuleAssets.Sprites.Actors.PlayerSheet.Clips.Idle);
```

Animation, tweens and particles are simulation state counted in fixed steps. They mean the same at any
frame rate, and a headless test can assert on them. A `Tween` is an eased value, and a `Countdown` is the
same timer with no value to read.

A game that needs geometry no renderer draws subclasses `Renderer` and writes into the `FrameView` it is
handed. When `Draw` runs is in [`architecture.md`](architecture.md#simulation-and-host).

## Hiding, fading and flashing

`Entity.Visible` and `Entity.Tint` hide and colour an entity and everything beneath it.
`Renderer.Visible` and a renderer's own `Color` do the same for one renderer. `Entity.Flash` mixes every
sprite beneath the entity towards `Entity.FlashColor`. None of them stops the entity stepping:

```csharp
Flash = grace > 0 ? 1f - Math.Min(sinceHit / (float)_tuning.HurtFlashTicks, 1f) : 0f;
Visible = Flash > 0f || grace / _tuning.BlinkTicks % 2 == 0;
```

## Your own shader

A shader is a fragment function authored in HLSL as `<name>.fx` under `Assets/`. It declares its
parameters and exactly one `float4 Fragment(SpritePixel pixel)`, which returns a premultiplied colour:

```hlsl
float Amount;

float4 Fragment(SpritePixel pixel)
{
    float4 color = pixel.Texel * pixel.Tint;
    float grey = dot(color.rgb, float3(0.299, 0.587, 0.114));
    color.rgb = lerp(color.rgb, grey.xxx, Amount);
    return color;
}
```

| Name | What it is |
| --- | --- |
| `pixel.Texel` | The sprite's premultiplied texel. An `r8` texture reads `(v, v, v, v)`, a coverage mask the tint colours. |
| `pixel.Tint` | The sprite's premultiplied tint. |
| `pixel.UV` | The texture coordinate, which on an atlas page is the page's. |
| `pixel.TextureSize` | The sprite's texture size in texels, which for a packed sprite is its atlas page's. |
| `Sample(texture, uv)` | Reads a texture parameter, clamped at its edges, with its own sampling or else the frame's. An `r8` texture's value is in `.r`. |
| `SampleSprite(uv)` | Reads the sprite's texture at another point. |
| `TextureSize(texture)` | A texture parameter's size in texels. |

A parameter is a global `float`, `float2`, `float3`, `float4` or `Texture2D`. Names starting with
`Capsule` are the engine's. A construct OpenGL 2.1 lacks, such as `Load` or `GetDimensions`, fails the
build naming the fix.

A `Material` binds a shader with its parameter values. `Renderer.Material` draws a renderer with it, and
`TileMap.Material` draws a whole tile map with it. An entity that assigns a material after it has started
declares it from `CollectAssets` ([`assets.md`](assets.md#loading-and-residency)). Draw order never
changes for a material. Lines, the light map and the debug overlay draw with the engine's own
shader.

## Two layers, and draw order

A frame carries two ordered lists. The world layer is placed by the camera and culled against it. The
screen layer is in canvas pixels, culled against `Run.Canvas`, and draws over the world layer. A
`ScreenEntity` and every renderer it holds are on the screen layer, and anything else is in the world.

Within a layer, what draws later has the higher sum of the entity's `ZIndex` up its ancestry and the
renderer's own `ZIndex`. Ties break by file order in a document and then by attachment order. A top-down
scene sets `Scene.YSort` to order world renderers in one band by their root entity's Y.

A `ScreenEntity` is placed by an `Anchor`, a fraction of the canvas on each axis, plus an offset in canvas
pixels. It keeps its distance from the edge it was anchored to whatever the canvas is:

```csharp
private readonly HealthBar _healthBar = new(Anchor.TopLeft, new Vector2(8f, 8f));
```

Menus are `Focusable` components under one `FocusNavigator`, which moves focus from the game's own focus
actions and the pointer.

## Parallax

An entity's `ScrollFactor` scales how far it moves with the camera about `Camera.ScrollCenter`. Zero on
both axes pins the entity to the screen, and one is the world. A camera centred on its scroll centre
draws every layer as authored. Draw order is the same `ZIndex` sum whatever the factor:

```csharp
public Sky(EntitySpawn spawn)
    : base(spawn)
{
    ScrollFactor = Vector2.Zero;
    ZIndex = -20;
    Add(new SpriteRenderer(Dusk) { Tiling = new Vector2(float.PositiveInfinity, 0f) });
}
```

A document may author the factor instead, as `scrollFactor` on an entry. A `Tiling` of positive infinity
on an axis repeats the frame without bound along it. A component that answers at the authored position,
such as a collider, a `TileMapCollider2D` or a `VisibleOnScreenNotifier2D`, refuses a factor other than
one. A tile map without a collider scrolls like any other entity.

## Lighting

A scene lowers the light with `Scene.Ambient`. A `PointLight` draws into the frame's light map, which the
host multiplies over the world layer. A world sprite drawn with `BlendMode.Additive` in a lit frame lights
the map too. A glow then stays bright in a dim room. The screen layer is never lit. A scene with white
ambient and no light runs no pass.

```csharp
Add(new PointLight { Radius = 56f, Color = HeadColor, Offset = new Vector2(0f, -PostSize.Y) });
Entity head = new(this, new Vector2(0f, -PostSize.Y - 2f)) { Scale = new Vector2(GlowScale) };
head.Add(new SpriteRenderer(Glow) { Color = HeadColor, Blend = BlendMode.Additive });
```

## Text

Capsule draws text from a bitmap font baked to texture pages. A run is laid out left to right, one glyph
per codepoint, with the kerning the font declares. There is no shaping, no bidirectional layout and no
distance field.

```csharp
Add(new Label(CapsuleAssets.Fonts.MenuFont, "Minimal Game")
{
    Pivot = Pivot.Top,
    HorizontalAlignment = HorizontalAlignment.Center,
});
```

`BitmapFont.Default` ships inside the runtime and needs no asset. Other fonts are authored under `Assets/`
([`assets.md`](assets.md#fonts)). `GlyphRun` is the layout every placement comes from, for code that
emits its own per-glyph sprites.

## Visibility

A `VisibleOnScreenNotifier2D` raises `ScreenEntered` and `ScreenExited` as a rect on its entity meets the
camera's visible region. It is how a bullet despawns when it leaves the screen.
