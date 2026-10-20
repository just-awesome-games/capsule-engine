using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Tests.Physics;

/// <summary>What finding a moved collider's contact candidates costs in proxies visited, the one quantity that reads the same on every machine.</summary>
public sealed class ContactPairTests
{
    private const string CrowdLayer = "crowd";
    private const string SensorLayer = "sensor";
    private const string OtherLayer = "other";

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

    // A component re-applies its layer each time its entity joins a scene. Setting Detects to the
    // layers it already detects keeps the collider's candidates and costs no pairing. Adding a layer
    // pairs the collider once and keeps what it touched.
    [Fact]
    public void SettingDetectsToTheLayersItAlreadyDetects_PairsNothing()
    {
        const int Crowd = 16;
        Scene scene = new();
        Prober sensor = new(Vector2.Zero, new Vector2(32f, 32f), CrowdLayer);
        sensor.Collider.Layer = SensorLayer;
        sensor.Collider.ReportsContacts = true;
        scene.Add(sensor);
        for (int index = 0; index < Crowd; index++)
        {
            Prober member = new(new Vector2(8f, 8f));
            member.Collider.Layer = CrowdLayer;
            scene.Add(member);
        }

        using SimulationHost host = new(scene);
        host.Step();
        scene.Collision.ResetDiagnostics();
        sensor.Collider.Detects = new(CrowdLayer);
        sensor.Collider.Detects = sensor.Collider.Detects.With(CrowdLayer);
        host.Step();

        Assert.Equal(0, scene.Collision.PairProxiesVisited);
        Assert.Equal(Crowd, sensor.Collider.Touching.Length);

        sensor.Collider.Detects = sensor.Collider.Detects.With(CollisionFixtures.Solid);
        host.Step();

        Assert.Equal(Crowd, scene.Collision.PairProxiesVisited);
        Assert.Equal(Crowd, sensor.Collider.Touching.Length);
    }

    // The pair query visits a layer for the colliders on it only while one of them detects the moved
    // collider's layer. A sensor that stops detecting the crowd, by any route, leaves the crowd's moves
    // passing over the bystander that shares its layer. The bystander reports contacts, which settles
    // the pairs once the sensor is gone.
    [Theory]
    [InlineData("detects", 0)]
    [InlineData("layer", 1)]
    [InlineData("removed", 0)]
    [InlineData("disabled", 0)]
    public void ADetectorThatStopsDetectingALayer_LeavesThatLayersMovesUnpaired(string stop, int visitsPerMember)
    {
        const int Crowd = 16;
        Scene scene = new();
        Prober sensor = DetectorOf(scene, Vector2.Zero);
        Prober bystander = new(Vector2.Zero, new Vector2(32f, 32f), CollisionFixtures.Solid);
        bystander.Collider.Layer = SensorLayer;
        bystander.Collider.ReportsContacts = true;
        scene.Add(bystander);
        Prober[] crowd = CrowdAt(scene, new Vector2(8f, 8f), Crowd);

        using SimulationHost host = new(scene);
        host.Step();
        switch (stop)
        {
            case "detects":
                sensor.Collider.Detects = sensor.Collider.Detects.Without(CrowdLayer).With(CollisionFixtures.Solid);
                break;
            case "layer":
                sensor.Collider.Layer = OtherLayer;
                break;
            case "removed":
                scene.Remove(sensor);
                break;
            default:
                sensor.Collider.Enabled = false;
                break;
        }

        host.Step();
        scene.Collision.ResetDiagnostics();
        Shift(crowd, new Vector2(8f, 0f));
        host.Step();

        Assert.Equal(visitsPerMember * Crowd, scene.Collision.PairProxiesVisited);
    }

    // A crowd that arrives from beyond the sensor's reach is found by its own pair query alone. Each
    // way a detector's record can change still leaves the crowd's moves pairing with every detector
    // that remains.
    [Theory]
    [InlineData("readded")]
    [InlineData("second leaves")]
    [InlineData("layer away")]
    [InlineData("layer back")]
    public void AnArrivingCrowd_PairsWithEveryRemainingDetector(string churn)
    {
        const int Crowd = 16;
        Scene scene = new();
        Prober sensor = DetectorOf(scene, Vector2.Zero);
        Prober second = DetectorOf(scene, Vector2.Zero);
        Prober[] crowd = CrowdAt(scene, new Vector2(400f, 0f), Crowd);

        using SimulationHost host = new(scene);
        host.Step();
        switch (churn)
        {
            case "readded":
                scene.Remove(sensor);
                host.Step();
                scene.Add(sensor);
                break;
            case "second leaves":
                break;
            case "layer away":
                sensor.Collider.Layer = OtherLayer;
                break;
            default:
                sensor.Collider.Layer = OtherLayer;
                host.Step();
                sensor.Collider.Layer = SensorLayer;
                break;
        }

        scene.Remove(second);
        host.Step();
        Shift(crowd, new Vector2(-392f, 8f));
        host.Step();

        Assert.Equal(Crowd, sensor.Collider.Touching.Length);
    }

    private static Prober DetectorOf(Scene scene, Vector2 position)
    {
        Prober detector = new(position, new Vector2(32f, 32f), CrowdLayer);
        detector.Collider.Layer = SensorLayer;
        detector.Collider.ReportsContacts = true;
        scene.Add(detector);

        return detector;
    }

    private static Prober[] CrowdAt(Scene scene, Vector2 position, int count)
    {
        Prober[] crowd = new Prober[count];
        for (int index = 0; index < count; index++)
        {
            crowd[index] = new Prober(position);
            crowd[index].Collider.Layer = CrowdLayer;
            scene.Add(crowd[index]);
        }

        return crowd;
    }

    private static void Shift(Prober[] crowd, Vector2 by)
    {
        foreach (Prober member in crowd)
        {
            member.Position += by;
        }
    }
}
