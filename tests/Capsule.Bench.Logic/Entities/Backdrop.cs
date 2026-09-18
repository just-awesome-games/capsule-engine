using Capsule.Bench.Logic.Components;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Bench.Logic.Entities;

// Its scroll factor is the document's, so the row is a scrolled run drawn by a virtual camera.
public sealed class Backdrop : Entity
{
    public Backdrop(EntitySpawn spawn)
        : base(spawn) =>
        Add(new ParallaxRow());
}
