# Shaders

After this page you can write a fragment shader, draw a renderer or a tile map with it, and share a value
across every material of a shader. Each type's XML docs hold its rules.

A shader is a fragment function the build wraps in the engine's sprite shader. The engine keeps the
vertex stage ([`vertex.hlsl`](../src/Capsule.Runtime/Rendering/Shaders/vertex.hlsl)), the pixel stage around
the fragment ([`pixel.hlsl`](../src/Capsule.Runtime/Rendering/Shaders/pixel.hlsl)), the batching, the tint
and the flash. What a pixel becomes is the game's.

| Need | Piece |
| --- | --- |
| A fragment function | An `.fx` file under `Assets/`, named in code as a `CapsuleAssets` member |
| A shader with its parameter values | `Material` |
| One renderer drawn with it, or all of an entity's | `Renderer.Material`, `Entity.Material` |
| A value every material of a shader shares | `Scene.ShaderGlobals` |
| The picture already drawn behind a sprite | `SampleScreen` |

## The fragment

A shader declares its parameters and exactly one `float4 Fragment(SpritePixel pixel)`, which returns a
premultiplied colour:

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
| `pixel.Position` | How far this pixel is from the quad's top-left corner, in the sprite's units. On a polygon, how far it is from the polygon's first point. |
| `pixel.Size` | The quad's size, in the sprite's units. Zero on a polygon. |
| `Sample(texture, uv)` | Reads a texture parameter, named as declared, with (0, 0) and (1, 1) its own corners whether or not an atlas packed it. A coordinate past an edge reads the edge. An `r8` texture's value is in `.r`. A texture passed to a function of the shader's own cannot be sampled there. |
| `SampleSprite(uv)` | Reads the sprite's texture at another point. |
| `Texel(texture, xy)` | Reads the one texel at a whole coordinate of a texture parameter, counted from its own top-left corner. No sampling mixes it with a neighbour, and a coordinate past an edge reads the edge. |
| `TextureSize(texture)` | A texture parameter's own size in texels, named as declared. |
| `SampleScreen(offset)` | Reads the picture already drawn at this pixel, moved by `offset` in the sprite's units. |

A parameter is a `float`, `float2`, `float3`, `float4` or `Texture2D` declared at file scope. Names
starting with `Capsule` are the engine's. A fragment reads a texture only through `Sample`, `Texel` and
`TextureSize`, and the build refuses `Load`, `GetDimensions` and indexing, which would read an atlas
page in place of a texture packed onto it.

## Drawing with it

An entity that assigns a material after it has started declares it from `CollectAssets`
([`assets.md`](assets.md#loading-and-residency)). A material never changes draw order. Lines, the light
map and the debug overlay draw with the engine's own shader.

## Reading the picture

A fragment that calls `SampleScreen` sees everything drawn before it, and its `ZIndex` decides what that is. A rect
below the actors shifts the background and leaves them whole. A rect over the world, on the screen layer, is an effect
on the whole frame:

```hlsl
float Rise;

float4 Fragment(SpritePixel pixel)
{
    float wave = sin((pixel.Position.y + Rise) * 0.4);
    return SampleScreen(float2(wave * 2.0, 0.0));
}
```

```csharp
Material ripple = new(CapsuleAssets.Shaders.RippleShader);
Add(new ColorRect(new Vector2(256f, 32f)) { Material = ripple });
```

- The engine copies the pixels a run of sprites can reach just before it draws. A frame that draws no such sprite, one
  off screen included, copies nothing.
- Sprites next to each other with one material share a copy and do not see each other. Anything drawn between them
  starts a new copy.
- A read more than 64 units past its sprite repeats the edge of the copy.
- A world material reads the picture before lighting, and its additive sprites light with the engine's shader.
- A canvas declared apart from the render resolution draws the screen layer on the window, where a read shows nothing.

## Scene globals

A shader declares a value every material shares in a `cbuffer SceneGlobals` block, and the scene sets
it:

```hlsl
cbuffer SceneGlobals
{
    float3 WorldAdd;
};
```

```csharp
Scene.ShaderGlobals.Set("WorldAdd", new Vector3(0.4f, 0.4f, 0.4f));
```

Each scene owns its globals.
