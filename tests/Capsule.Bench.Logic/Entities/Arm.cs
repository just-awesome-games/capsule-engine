using System.Numerics;
using Capsule.Bench.Logic.Components;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

// Carries a sprite so the composed transform reaches the intent stream.
public sealed class Arm : Entity
{
    public Arm(Entity parent)
        : base(parent, new Vector2(12f, 0f))
    {
        Scale = new Vector2(0.25f, 0.25f);
        Add(new SpriteRenderer(SpriteField.Tile));
    }
}
