// Capsule's sprite pixel stage, which every sprite shader shares. The build writes a sampling header
// above it for the fragment it wraps, declaring the sprite's texture and the fragment's own, and puts
// the game's Fragment, or the engine's own in sprite.fx, at the marked line.
//
// A single-channel texture samples as (v, 0, 0, 1). The sprite's texel takes the premultiplied coverage
// form (v, v, v, v), which draws white at opacity v under the tint.
//
// The flash mixes the fragment's colour towards the flash colour scaled by the texel's coverage. The
// vertex carries that colour already premultiplied by the tint's alpha. A full flash draws a
// premultiplied silhouette under alpha blending and adds it under additive blending, where the
// fragment's alpha stays zero. A zero amount leaves the colour exactly as the fragment returned it.
// The inputs are in location order: tint, flash, texture coordinate, coverage. Coverage comes from the
// vertex stage. A pixel stage reads no uniform of the engine's.

float2 TextureSize(Texture2D source)
{
    uint width;
    uint height;
    source.GetDimensions(width, height);
    return float2(width, height);
}

float4 SampleSprite(float2 uv)
{
    float4 texel = Sample(SpriteTexture, uv);
    return CapsuleSpriteCoverage > 0.5 ? texel.rrrr : texel;
}

struct SpritePixel
{
    float4 Texel;
    float4 Tint;
    float2 UV;
    float2 TextureSize;
};

// <Fragment>

float4 CapsulePixel(float4 tint : COLOR0, float4 flash : COLOR1, float2 uv : TEXCOORD0, float coverage : TEXCOORD1) : SV_Target0
{
    CapsuleSpriteCoverage = coverage;
    SpritePixel pixel;
    pixel.Texel = SampleSprite(uv);
    pixel.Tint = tint;
    pixel.UV = uv;
    pixel.TextureSize = TextureSize(SpriteTexture);

    float4 color = Fragment(pixel);
    color.rgb = lerp(color.rgb, flash.rgb * pixel.Texel.a, flash.a);
    return color;
}
