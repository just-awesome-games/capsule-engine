// Shifts whole rows of the picture behind the sprite, in a wave that rises and fades toward its top. Rise
// is rows risen.
float Rise;

float4 Fragment(SpritePixel pixel)
{
    float row = floor(pixel.Position.y);
    float wave = 1.0 - 4.0 * abs(frac((row + Rise) / 16.0) - 0.5);
    float fade = (row + 1.0) / pixel.Size.y;
    return SampleScreen(float2(round(wave * 2.0 * fade), 0.0));
}
