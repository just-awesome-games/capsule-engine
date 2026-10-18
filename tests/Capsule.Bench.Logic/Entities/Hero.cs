namespace Capsule.Bench.Logic.Entities;

public sealed class Hero : Entity
{
    public Hero(EntitySpawn spawn)
        : base(spawn) =>
        Add(new SpriteRenderer(CapsuleAssets.Sprites.WalkerSheet.Frames.Walk0));

    protected override void OnStep(in StepContext context) => Position += Vector2.UnitX;
}
