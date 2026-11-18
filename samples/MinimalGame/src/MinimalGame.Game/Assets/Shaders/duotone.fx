// Maps each texel's brightness onto a ramp from Shadow to Light. The scene sets the ramp, and every
// material drawing this shader in it shares that one palette. The light at the pixel brightens the texel
// before it is mapped, and a lit tile moves up the ramp and keeps to the palette. Its scene is unlit, and the
// light is read here instead of multiplying the ramp's colour.
cbuffer SceneGlobals
{
    float4 Shadow;
    float4 Light;
};

float4 Fragment(SpritePixel pixel)
{
    float4 color = pixel.Texel * pixel.Tint;
    float shade = dot(color.rgb * SampleLight().rgb, float3(0.299, 0.587, 0.114)) / max(color.a, 0.0001);
    return lerp(Shadow, Light, saturate(shade)) * color.a;
}
