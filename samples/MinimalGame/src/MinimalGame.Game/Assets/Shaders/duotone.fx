// Maps each texel's brightness onto a ramp from Shadow to Light.
float4 Shadow;
float4 Light;

float4 Fragment(SpritePixel pixel)
{
    float4 color = pixel.Texel * pixel.Tint;
    float shade = dot(color.rgb, float3(0.299, 0.587, 0.114)) / max(color.a, 0.0001);
    return lerp(Shadow, Light, shade) * color.a;
}
