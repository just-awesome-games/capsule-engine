// Tints each texel by one row of Ramp, the row Row names, as a palette material draws one row of its
// table. Every material of this shader binds a texture and a value of its own.
Texture2D Ramp;
float Row;

float4 Fragment(SpritePixel pixel)
{
    float4 tint = Sample(Ramp, float2(0.5, (Row + 0.5) / TextureSize(Ramp).y));

    return pixel.Texel * pixel.Tint * tint;
}
