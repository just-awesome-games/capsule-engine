using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Components;
using Capsule.Bench.Logic.Entities;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>A parked camera over one <see cref="SpriteField"/>; a subclass states its count and is a workload.</summary>
public abstract class SpriteFieldScene : Scene
{
    protected SpriteFieldScene(int count, bool switches = false)
        : this(SpriteField.Tile, 8f, World.ViewportSize, count, switches)
    {
    }

    protected SpriteFieldScene(Sprite frame, float extent, Vector2 viewport, int count, bool switches = false)
    {
        Camera = new ParkedCamera(viewport / 2f, viewport);
        Add(new Holder(new SpriteField(frame, extent, viewport, count, switches)));
    }
}
