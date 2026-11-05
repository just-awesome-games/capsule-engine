// The paused room behind the menu, drained of most of its colour and dimmed.
float4 Fragment(SpritePixel pixel)
{
    float4 room = SampleScreen(float2(0.0, 0.0));
    float shade = dot(room.rgb, float3(0.299, 0.587, 0.114));
    return float4(lerp(shade.xxx, room.rgb, 0.25) * 0.45, 1.0);
}
