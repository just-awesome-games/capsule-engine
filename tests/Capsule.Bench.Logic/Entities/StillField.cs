using Capsule.Bench.Logic.Components;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Bench.Logic.Entities;

public sealed class StillField : Entity
{
    public StillField(EntitySpawn spawn)
        : base(spawn) =>
        Add(new StillSprites());
}
