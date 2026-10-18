# Shaders

After this page you can write a fragment shader, draw a renderer or a tile map with it, and share a value
across every material of a shader. Each type's XML docs hold its rules.

A shader is a fragment function the build wraps in the engine's sprite shader. The engine keeps the
vertex stage, the batching, the tint and the flash. What a pixel becomes is the game's.

| Need | Piece |
| --- | --- |
| A fragment function | An `.fx` file under `Assets/`, named in code as a `CapsuleAssets` member |
| A shader with its parameter values | `Material` |
| A renderer or a tile map drawn with it | `Renderer.Material`, `TileMap.Material` |
| A value every material of a shader shares | `Scene.ShaderGlobals` |

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
| `Sample(texture, uv)` | Reads a texture parameter. An `r8` texture's value is in `.r`. |
| `SampleSprite(uv)` | Reads the sprite's texture at another point. |
| `TextureSize(texture)` | A texture parameter's size in texels. |

A parameter is a `float`, `float2`, `float3`, `float4` or `Texture2D` declared at file scope. Names
starting with `Capsule` are the engine's. A construct OpenGL 2.1 lacks, such as `Load` or
`GetDimensions`, fails the build naming the fix.

## Drawing with it

An entity that assigns a material after it has started declares it from `CollectAssets`
([`assets.md`](assets.md#loading-and-residency)). A material never changes draw order. Lines, the light
map and the debug overlay draw with the engine's own shader.

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
