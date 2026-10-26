float4 Fragment(SpritePixel pixel)
{
    return pixel.Texel * pixel.Tint;
}
