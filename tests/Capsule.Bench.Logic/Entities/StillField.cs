using Capsule.Bench.Logic.Components;

namespace Capsule.Bench.Logic.Entities;

public sealed class StillField : Entity
{
    public StillField(EntitySpawn spawn)
        : base(spawn) =>
        Add(new StillSprites());
}
