using System.Numerics;
using Capsule;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game;

public sealed class Marker : Entity
{
    private static readonly Vector2 Body = new(8f, 8f);

    public Marker(EntitySpawn spawn)
        : base(spawn.Position) =>
        Add(new QuadRenderer(Body, new ColorRgba(0xE0, 0x6C, 0x2A)));

    public override void Update(in StepContext context)
    {
        if (context.Input.IsHeld(ConsumerInput.Advance))
        {
            Position += Vector2.UnitX;
        }
    }
}
