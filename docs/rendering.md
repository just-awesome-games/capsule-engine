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

A pixel-art game declares one render surface and lets the canvas follow it. The interface is then drawn
in the same pixels as the world:

```csharp
return CapsuleBoot.Configure("Minimal Game", new DesktopPlatform())
    .WithCommandLine(args)
    .WithRunStart(GameBoot.Start)
    .WithRenderResolution(320, 180)
    .WithSampling(TextureSampling.Point)
    .RunScene<MainMenu>();
```

`Camera.Fit` decides what an output whose aspect ratio differs from the viewport shows. On a declared
render surface the world is drawn at the declared pixels per unit under either sampling. Point sampling
snaps each sprite to the surface's pixel grid and presents the surface at an integer scale when the
output can hold it.

## A camera that follows

A document's `camera` key installs a camera, or a scene's constructor sets one directly. A subclass
sets its feel once and names its subject in `OnStart`:

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

`Camera` documents how each framing lever composes with the follow. A subclass retargets, zooms or
recoils in `OnLateStep`, which runs before the follow.

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
SpriteRenderer sprite = new(CapsuleAssets.Sprites.Actors.PlayerSheet.Frames.Idle0);
Add(sprite);
Muzzle = sprite.Socket(CapsuleAssets.Sprites.Actors.PlayerSheet.Sockets.Muzzle);

_animator = new SpriteAnimator(sprite);
Add(_animator);
```

A `SpriteAnimator` plays a clip on a `SpriteRenderer`. Clips are held in whole fixed steps.
Animation is simulation state and means the same at any frame rate. `Play` does not restart a clip that
is already playing, and a step may ask for the clip its state implies every step:

```csharp
_animator.Play(velocity.X != 0f ? CapsuleAssets.Sprites.Actors.PlayerSheet.Clips.Walk : CapsuleAssets.Sprites.Actors.PlayerSheet.Clips.Idle);
```

A `Tween` is an eased value that counts its duration in whole fixed steps. It drives a flash, a slide
or any one-off eased value. A `Countdown` is the same timer with no value to read, for a cooldown, a
delay or a lifetime.

A game that needs geometry no renderer draws subclasses `Renderer` and writes into the `FrameView` it
is handed. The sheet format, atlases and where sprites come from are [`assets.md`](assets.md).

## Hiding, fading and flashing

`Entity.Visible` and `Entity.Tint` hide and colour an entity and everything beneath it.
`Renderer.Visible` and a renderer's own `Color` do the same for one renderer. `Entity.Flash` mixes
every sprite beneath the entity towards `Entity.FlashColor`, white by default, after the tint and after
any material. At 1 a sprite is a silhouette of that colour. None of them stops the entity stepping:

```csharp
Flash = grace > 0 ? 1f - Math.Min(sinceHit / (float)_tuning.HurtFlashTicks, 1f) : 0f;
Tint = grace > 0 ? _tuning.HurtTint : ColorRgba.White;
Visible = Flash > 0f || grace / _tuning.BlinkTicks % 2 == 0;
```

## Your own shader

A shader is a fragment function authored as `<name>.fx` under `Assets/` in HLSL
([`assets.md`](assets.md#shaders)). It declares its parameters and exactly one
`float4 Fragment(SpritePixel pixel)`, and returns a premultiplied colour. `pixel.Texel` is the
sprite's premultiplied texel, `pixel.Tint` its premultiplied tint and `pixel.UV` its texture
coordinate, which on an atlas page is the page's. A parameter is a global `float`, `float2`,
`float3`, `float4` or `Texture2D`, and reads zero until a material sets it. `Sample(texture, uv)`
reads a texture parameter with the frame's sampling, clamped at its edges, and `SampleSprite(uv)`
reads the sprite's texture at another point. A stone-statue look:

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

A `Material` binds it with its parameter values, and `Renderer.Material` draws a renderer with it:

```csharp
Material stone = new(CapsuleAssets.Shaders.DesaturateShader);
stone.Set("Amount", 1f);
sprite.Material = stone;
```

Draw order never changes for a material. Neighbouring sprites draw in one batch when they share a
texture and a material instance, so renderers that look alike share one `Material`. Tint, blend mode
and flash never split a batch. Lines, the light map and the development overlay draw with the engine's
own shader.

## Two layers, and draw order

A frame carries two ordered lists. The world layer is placed by the camera and culled against it. The
screen layer is in canvas pixels, culled against `Run.Canvas`, and draws over the world layer. An
entity's type decides its layer: a `ScreenEntity` is on the screen layer, anything else is in the
world, and every renderer it holds follows.

Within a layer, what draws later has the higher sum of the entity's `ZIndex` up its ancestry and the
renderer's own `ZIndex`. Ties break by file order in a document and then by attachment order.

A top-down scene sets `Scene.YSort`, and world renderers in one band then draw in order of their root
entity's Y, a root's children with it. A floor and a canopy take bands of their own, below and above.

A `ScreenEntity` is placed by an `Anchor`, a fraction of the canvas on each axis, plus an offset in
canvas pixels. An element keeps its distance from the edge it was anchored to whatever the canvas is:

```csharp
private readonly HealthBar _healthBar = new(Anchor.TopLeft, new Vector2(8f, 8f));
```

Menus are `Focusable` components under one `FocusNavigator`. The navigator owns which item has focus
and moves it from the game's own focus actions, pointer included.

## Parallax

An entity may carry a `ScrollFactor`, and the host draws it by a virtual camera whose centre is moved
by that factor about `Camera.ScrollCenter`. Zero on both axes pins the entity to the screen, and one
is the world. The scroll centre defaults to half the camera's `ViewportSize`, and a camera centred there
draws every layer as authored. A layer holds its place at any output aspect or zoom. Draw order is the same `ZIndex` sum whatever the factor:

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

## Particles

A `ParticleEmitter` is a renderer that steps a fixed pool of sprite particles on the fixed tick. Its
documented example is a complete burst effect. A continuous emitter sets `Rate` or `RateOverDistance`, and a burst
calls `Emit`. Particles are simulation state. A headless run reproduces a burst exactly, and a test can
assert on one. Local space, noise, sub-emission, collision and trails are not built.

## Lighting

```csharp
Add(new ColorRect(HeadSize) { Color = HeadColor, Blend = BlendMode.Additive, Offset = HeadOffset });
Add(new PointLight { Radius = 56f, Color = HeadColor, Offset = new Vector2(0f, -PostSize.Y) });
```

A scene lowers the light with `Scene.Ambient`, set in code or by the document's `ambient`. A
`PointLight` draws into the frame's light map, and the host multiplies that map over the world layer in
one pass. In a lit frame a world sprite drawn with `BlendMode.Additive` lights the map too, and a glow
sprite never goes dark in a dim room. The screen layer is never lit. A scene with white ambient and no
light runs no pass. Shadows, normal maps, bloom, a light-map scale and a per-renderer opt-out are not
built.

## Text

Capsule draws text from a bitmap font: a font baked to texture pages with a glyph rectangle per
codepoint. Nothing is rasterized at run time and no outline font is opened. A run is laid out left to
right, one glyph per codepoint, with the kerning the font declares. There is no shaping, no
bidirectional layout and no distance field.

```csharp
Add(new Label(CapsuleAssets.Fonts.MenuFont, "Minimal Game")
{
    Pivot = Pivot.Top,
    HorizontalAlignment = HorizontalAlignment.Center,
});
```

`BitmapFont.Default` ships inside the runtime and needs no asset. Other fonts are authored under
`Assets/` ([`assets.md`](assets.md#fonts)). `GlyphRun` is the layout pass every placement comes
from. Code that emits its own per-glyph sprites enumerates it for the geometry the engine draws and
measures.

## Visibility

A `VisibleOnScreenNotifier2D` raises `ScreenEntered` and `ScreenExited` as a rect on its entity meets
the camera's visible region, settled once a step against the region that step's frame drew. It is how
a bullet despawns when it leaves the screen.
