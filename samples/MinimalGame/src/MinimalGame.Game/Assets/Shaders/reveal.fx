// Draws only the part of a figure within Reveal units above its feet. FromPivot measures from the pivot every
// renderer of the figure shares. A part trimmed to its own frame cuts off at the same height as the body.
float Reveal;

float4 Fragment(SpritePixel pixel)
{
    float height = -pixel.FromPivot.y;
    return height < Reveal ? pixel.Texel * pixel.Tint : float4(0.0, 0.0, 0.0, 0.0);
}
