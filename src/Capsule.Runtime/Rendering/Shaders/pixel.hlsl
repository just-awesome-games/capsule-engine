// Capsule's sprite pixel stage, which every sprite shader shares. The build writes a sampling header
// above it for the fragment it wraps, declaring the sprite's texture and the fragment's own with the
// Sample and TextureSize that read them, and puts the game's Fragment, or the engine's own in sprite.fx,
// at the marked line.
//
// A single-channel texture samples as (v, 0, 0, 1). The sprite's texel takes the premultiplied coverage
// form (v, v, v, v), which draws white at opacity v under the tint.
//
// The flash mixes the fragment's colour towards the flash colour scaled by the texel's coverage. The
// vertex carries that colour already premultiplied by the tint's alpha. A full flash draws a
// premultiplied silhouette under alpha blending and adds it under additive blending, where the
// fragment's alpha stays zero. A zero amount leaves the colour exactly as the fragment returned it.
// The inputs are in location order: tint, flash, texture coordinate, coverage, quad, offset from the
// pivot. Coverage, the quad and the offset come from the vertex stage. A pixel stage reads no uniform
// of the engine's unless its fragment calls SampleScreen, whose uniforms the renderer pushes with the
// copy it makes.

float4 SampleSprite(float2 uv)
{
    float4 texel = Sample(SpriteTexture, uv);
    return CapsuleSpriteCoverage > 0.5 ? texel.rrrr : texel;
}

// The picture already drawn at this pixel, moved by offset in the sprite's units. A read past what the
// renderer copied for it, the sprite's bounds and a margin, repeats the copy's edge.
float4 SampleScreen(float2 offset)
{
    float2 at = clamp(CapsuleScreenPixel + (offset * CapsuleScreenScale), CapsuleScreenArea.xy, CapsuleScreenArea.zw);
    return Sample(CapsuleScreen, at / TextureSize(CapsuleScreen));
}

struct SpritePixel
{
    float4 Texel;
    float4 Tint;
    float2 UV;
    float2 TextureSize;
    float2 Position;
    float2 Size;
    float2 FromPivot;
};

// <Fragment>

float4 CapsulePixel(float4 tint : COLOR0, float4 flash : COLOR1, float2 uv : TEXCOORD0, float coverage : TEXCOORD1, float4 quad : TEXCOORD2, float2 fromPivot : TEXCOORD3, float4 surface : SV_Position) : SV_Target0
{
    CapsuleSpriteCoverage = coverage;
    CapsuleScreenPixel = surface.xy;
    SpritePixel pixel;
    pixel.Texel = SampleSprite(uv);
    pixel.Tint = tint;
    pixel.UV = uv;
    pixel.TextureSize = TextureSize(SpriteTexture);
    pixel.Position = quad.xy;
    pixel.Size = quad.zw;
    pixel.FromPivot = fromPivot;

    float4 color = Fragment(pixel);
    color.rgb = lerp(color.rgb, flash.rgb * pixel.Texel.a, flash.a);
    return color;
}
