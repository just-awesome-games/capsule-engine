using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>A dark post topped by a warm additive glow and the point light that lights the room around it.</summary>
public sealed class Lamp : Entity
{
    private static readonly Vector2 PostSize = new(2f, 10f);
    private static readonly Vector2 HeadSize = new(4f, 4f);
    private static readonly ColorRgba PostColor = new(40, 40, 44);
    private static readonly ColorRgba HeadColor = new(255, 200, 140);

    public Lamp(EntitySpawn spawn)
        : base(spawn)
    {
        Add(new ColorRect(PostSize) { Color = PostColor, Offset = new Vector2(-PostSize.X / 2f, -PostSize.Y) });
        Add(new ColorRect(HeadSize)
        {
            Color = HeadColor,
            Blend = BlendMode.Additive,
            Offset = new Vector2(-HeadSize.X / 2f, -PostSize.Y - HeadSize.Y),
        });
        Add(new PointLight { Radius = 56f, Color = HeadColor, Offset = new Vector2(0f, -PostSize.Y) });
    }
}
