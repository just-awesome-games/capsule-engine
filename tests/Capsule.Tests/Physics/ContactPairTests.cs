using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Tests.Physics;

/// <summary>What finding a moved collider's contact candidates costs in proxies visited, the one quantity that reads the same on every machine.</summary>
public sealed class ContactPairTests
{
    private const string CrowdLayer = "crowd";
    private const string SensorLayer = "sensor";

    // A moved collider pairs with the layers it detects and the layers whose colliders detect its own.
    // A stacked crowd that another layer detects then passes over itself, and the detector still
    // touches every member through the pairs the movers made.
    [Fact]
    public void AStackedCrowdAnotherLayerDetects_PairsEachMoverWithTheDetectorAlone()
    {
        const int Crowd = 64;
        Scene scene = new();
        Prober sensor = new(Vector2.Zero, new Vector2(32f, 32f), CrowdLayer);
        sensor.Collider.Layer = SensorLayer;
        sensor.Collider.ReportsContacts = true;
        scene.Add(sensor);

        Prober[] crowd = new Prober[Crowd];
        for (int index = 0; index < Crowd; index++)
        {
            crowd[index] = new Prober(new Vector2(8f, 8f), CollisionFixtures.Solid);
            crowd[index].Collider.Layer = CrowdLayer;
            crowd[index].Collider.ReportsContacts = true;
            scene.Add(crowd[index]);
        }

        using SimulationHost host = new(scene);
        host.Step();
        scene.Collision.ResetDiagnostics();
        // Far enough to leave each fat box, still inside the sensor.
        foreach (Prober member in crowd)
        {
            member.Position += new Vector2(8f, 0f);
        }

        host.Step();

        Assert.Equal(Crowd, scene.Collision.PairProxiesVisited);
        Assert.Equal(Crowd, sensor.Collider.Touching.Length);
    }
}
