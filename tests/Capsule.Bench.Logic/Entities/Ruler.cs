using Capsule.Bench.Logic.Components;

namespace Capsule.Bench.Logic.Entities;

public sealed class Ruler : Entity
{
    public Ruler(EntitySpawn spawn)
        : base(spawn) =>
        Add(new LineStrokes());
}
