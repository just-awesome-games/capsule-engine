namespace MinimalGame.Game.Entities;

/// <summary>A dark post topped by a warm glow, the point light that lights the room around it and a hum heard nearby.</summary>
public sealed class Lamp : Entity
{
    private const float GlowScale = 1.5f;

    private static readonly Vector2 PostSize = new(2f, 10f);
    private static readonly ColorRgba PostColor = new(40, 40, 44);
    private static readonly ColorRgba HeadColor = new(255, 200, 140);

    public Lamp(EntitySpawn spawn)
        : base(spawn)
    {
        Add(new ColorRect(PostSize) { Color = PostColor, Offset = new Vector2(-PostSize.X / 2f, -PostSize.Y) });
        Add(new PointLight { Radius = 56f, Color = HeadColor, Offset = new Vector2(0f, -PostSize.Y) });
        Add(new AudioSource(CapsuleAssets.Audio.LampHumSound)
        {
            Bus = AudioBuses.Sfx,
            Loop = true,
            AutoPlay = true,
            Range = new AudioRange(200f) { Falloff = Ease.InQuad },
        });

        Entity head = new(this, new Vector2(0f, -PostSize.Y - 2f)) { Scale = new Vector2(GlowScale) };
        head.Add(Glow.Renderer(HeadColor));
    }
}
