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
// The inputs are in location order: tint, flash, texture coordinate with the light's after it, coverage,
// quad, offset from the pivot. Coverage, the quad, the offset and the light's coordinate come from the vertex
// stage. A pixel stage reads no uniform of the engine's unless its fragment calls SampleScreen, whose uniforms
// the renderer pushes with the copy it makes.
//
// The build compiles the stage in two forms. The lit form multiplies the colour, flash included, by the light at
// its pixel, and a world draw in a lit frame takes it unless its entity is unlit. Every other draw takes the
// unlit form, which leaves the colour as it is. On a world pass in a lit frame the renderer binds the whole light,
// and an unlit entity's draws get it too for SampleLight to read. Every other draw gets one white texel. A fragment
// that calls SampleScreen reads a picture that is already lit, and it compiles only in the unlit form.

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

// The light at this pixel, alpha 1: the scene's ambient colour where no light reaches, each light added over
// it with no ceiling, and white in a frame that runs no lighting and on the screen layer. The vertex stage
// places the pixel on the light, which is read by the nearest texel at its one level and needs no derivatives.
float4 SampleLight()
{
    return float4(CapsuleReadLight(CapsuleLightUV).rgb, 1.0);
}

// <Fragment>

float4 CapsulePixel(nointerpolation float4 tint : COLOR0, nointerpolation float4 flash : COLOR1, float4 uvs : TEXCOORD0, nointerpolation float coverage : TEXCOORD1, float4 quad : TEXCOORD2, float2 fromPivot : TEXCOORD3, float4 surface : SV_Position) : SV_Target0
{
    CapsuleSpriteCoverage = coverage;
    CapsuleScreenPixel = surface.xy;
    float2 uv = uvs.xy;
    CapsuleLightUV = uvs.zw;
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
    if (CapsuleStageLights)
    {
        color.rgb *= SampleLight().rgb;
    }

    return color;
}
