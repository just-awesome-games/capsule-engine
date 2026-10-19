namespace Capsule.Bench.Logic.Entities;

/// <summary>A sensor on its own layer that reports every bullet it touches, as a hero's hurt box would.</summary>
/// <remarks>Detecting the bullets' layer is what makes every moving bullet a pair for something.</remarks>
public sealed class Target : Entity
{
    public Target(Vector2 position)
        : base(position)
    {
        BoxCollider2D box = new(new Vector2(16f, 16f)) { Layer = CollisionLayers.Sensor, ReportsContacts = true };
        box.Detects = new(CollisionWorld2D.DefaultLayerName);
        Add(box);
    }
}
