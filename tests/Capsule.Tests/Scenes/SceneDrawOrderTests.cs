using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Spawning;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Scenes;

public sealed class SceneDrawOrderTests
{
    [Fact]
    public void ARuntimeEntityInALowerBand_DrawsUnderADocumentPlacedOne()
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.RoomWithoutTerrain(new EntityPlacement(1, "prop", 0f, 0f, ZIndex: 10)),
            SceneFixtures.Registry(("prop", Spawns(2))));

        Marker background = new() { ZIndex = -5 };
        background.Add(Tag(1));
        scene.Add(background);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2], Order(simulation));
    }

    [Fact]
    public void EntitiesSharingABand_KeepInsertionAndAttachmentOrder()
    {
        Marker first = new() { ZIndex = 3 };
        first.Add(Tag(11));
        first.Add(Tag(12));

        Marker second = new() { ZIndex = 3 };
        second.Add(Tag(21));
        second.Add(Tag(22));

        SceneFixtures.HookScene scene = new();
        scene.Add(first);
        scene.Add(second);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([11, 12, 21, 22], Order(simulation));
    }

    [Fact]
    public void ABandChangedAfterAttaching_ReordersTheFrame()
    {
        Marker first = new();
        first.Add(Tag(1));
        Marker second = new();
        SpriteRenderer offset = Tag(2);
        second.Add(offset);

        SceneFixtures.HookScene scene = new();
        scene.Add(first);
        scene.Add(second);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        Assert.Equal([1, 2], Order(simulation));

        first.ZIndex = 5;
        simulation.Step(SceneFixtures.Step());
        Assert.Equal([2, 1], Order(simulation));

        offset.ZIndex = 9;
        simulation.Step(SceneFixtures.Step());
        Assert.Equal([1, 2], Order(simulation));
    }

    [Fact]
    public void ARendererOffset_ComposesWithItsEntitysBand()
    {
        Marker banded = new() { ZIndex = 5 };
        banded.Add(Tag(2));

        Marker split = new();
        split.Add(Tag(1));
        split.Add(Tag(3, zIndex: 10));

        SceneFixtures.HookScene scene = new();
        scene.Add(banded);
        scene.Add(split);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2, 3], Order(simulation));
    }

    // The sum is widened, not wrapped: an int would carry int.MaxValue + 1 round to int.MinValue
    // and draw the nearer renderer under everything.
    [Fact]
    public void ASumPastAnInt_StillDrawsOverTheBandBelowIt()
    {
        Marker ceiling = new() { ZIndex = int.MaxValue };
        ceiling.Add(Tag(1));

        Marker beyond = new() { ZIndex = int.MaxValue };
        beyond.Add(Tag(2, zIndex: 1));

        SceneFixtures.HookScene scene = new();
        scene.Add(ceiling);
        scene.Add(beyond);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2], Order(simulation));
    }

    // The same pair the other way round: the larger key draws last whatever order they arrived in.
    [Fact]
    public void ASumPastAnInt_SortsAboveItsBandFromEitherInsertionOrder()
    {
        Marker beyond = new() { ZIndex = int.MaxValue };
        beyond.Add(Tag(2, zIndex: 1));

        Marker ceiling = new() { ZIndex = int.MaxValue };
        ceiling.Add(Tag(1));

        SceneFixtures.HookScene scene = new();
        scene.Add(beyond);
        scene.Add(ceiling);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2], Order(simulation));
    }

    // The traversal walks the sorted list against a cursor, so a key raised from inside a Draw
    // must not re-sort under it and hand the same renderer to the cursor a second time.
    [Fact]
    public void ARendererRaisingItsOwnKeyWhileDrawing_DrawsOnceThatStepAndLastOnTheNext()
    {
        Marker raising = new();
        raising.Add(new Raising(1, raisedTo: 5));

        Marker above = new() { ZIndex = 1 };
        above.Add(Tag(2));

        SceneFixtures.HookScene scene = new();
        scene.Add(raising);
        scene.Add(above);

        // The first frame is the one the raise is written during: it draws in the order that held
        // before the write, and exactly once.
        using SceneSimulation simulation = new(scene);

        Assert.Equal([1, 2], Order(simulation));

        // The raise landed; it just did not reorder the frame it was written during.
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([2, 1], Order(simulation));
    }

    // The same write, alongside a detach that rebuilds the list mid-traversal: the rebuild re-sorts
    // with the new key already written, so only the pass claim keeps the moved renderer from
    // drawing twice.
    [Fact]
    public void ARendererRaisingItsKeyAndDetachingAPeer_StillDrawsOnce()
    {
        Marker above = new() { ZIndex = 1 };
        SpriteRenderer detached = Tag(2);
        above.Add(detached);
        above.Add(Tag(3));

        Marker raising = new();
        raising.Add(new Raising(1, raisedTo: 5, detaches: detached));

        SceneFixtures.HookScene scene = new();
        scene.Add(raising);
        scene.Add(above);

        using SceneSimulation simulation = new(scene);

        Assert.Equal([1, 3], Order(simulation));
    }

    // Lowering a peer that has not drawn yet would, if it re-sorted at once, drop that peer behind
    // the cursor and lose it from the frame entirely. Deferring the sort keeps every renderer.
    [Fact]
    public void ARendererLoweringALaterPeerWhileDrawing_StillDrawsEveryRenderer()
    {
        Marker first = new();
        first.Add(Tag(1));

        Marker last = new() { ZIndex = 2 };
        SpriteRenderer lowered = Tag(3);
        last.Add(lowered);

        Marker middle = new() { ZIndex = 1 };
        middle.Add(new Lowering(2, lowered, loweredTo: -10));

        SceneFixtures.HookScene scene = new();
        scene.Add(first);
        scene.Add(middle);
        scene.Add(last);

        using SceneSimulation simulation = new(scene);

        Assert.Equal([1, 2, 3], Order(simulation));

        // And the write did land: it orders the next frame.
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([3, 1, 2], Order(simulation));
    }

    // Both mutations from one Draw: the lowered peer must still draw, and the detached one must
    // not. A list rebuilt mid-traversal would reorder around the cursor and lose the first.
    [Fact]
    public void ARendererLoweringOnePeerAndDetachingAnother_DrawsEveryRendererThatRemains()
    {
        Marker first = new();
        first.Add(Tag(1));

        Marker third = new() { ZIndex = 2 };
        SpriteRenderer lowered = Tag(3);
        third.Add(lowered);

        Marker fourth = new() { ZIndex = 3 };
        SpriteRenderer detached = Tag(4);
        fourth.Add(detached);

        Marker second = new() { ZIndex = 1 };
        second.Add(new Lowering(2, lowered, loweredTo: -10, detaches: detached));

        SceneFixtures.HookScene scene = new();
        scene.Add(first);
        scene.Add(second);
        scene.Add(third);
        scene.Add(fourth);

        using SceneSimulation simulation = new(scene);

        Assert.Equal([1, 2, 3], Order(simulation));

        // Both writes landed, and they order the next frame.
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([3, 1, 2], Order(simulation));
    }

    // The draw bookkeeping is the scene's, not the renderer's: an entity that drew under one
    // simulation must not be suppressed by the next one's first frame.
    [Fact]
    public void AnEntityMovedToAnotherSimulation_DrawsInThatSimulationsFirstFrame()
    {
        Marker traveller = new();
        traveller.Add(Tag(1));

        SceneFixtures.HookScene origin = new();
        origin.Add(traveller);
        using (SceneSimulation first = new(origin))
        {
            Assert.Equal([1], Order(first));
            origin.Remove(traveller);
        }

        SceneFixtures.HookScene destination = new();
        destination.Add(traveller);
        using SceneSimulation second = new(destination);

        Assert.Equal([1], Order(second));
    }

    [Fact]
    public void AnAuthoredBand_LandsOnEveryComposedEntity()
    {
        Scene scene = SceneFixtures.RoomScene(
            new SceneDocument(
                [
                    new TileMapPlacement(SceneFixtures.TerrainId, SceneFixtures.RoomGrid(), ZIndex: -20),
                    new EntityPlacement(1, "prop", 0f, 0f, ZIndex: 7),
                ],
                SceneFixtures.TerrainId + 1),
            SceneFixtures.Registry(("prop", Spawns(1))));

        Assert.Equal(-20, Assert.IsType<TileMap>(scene.Entities[0]).ZIndex);
        Assert.Equal(7, scene.Entities[1].ZIndex);
    }

    // The class owns the default: a placement authoring no band leaves the one the constructor
    // chose, and an authored band — 0 like any other — is what overrides it.
    [Theory]
    [InlineData(null, Banded.Band)]
    [InlineData(0, 0)]
    [InlineData(-4, -4)]
    public void AnAuthoredBand_OverridesTheClasssOwnOnlyWhenPresent(int? authored, int expected)
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.RoomWithoutTerrain(new EntityPlacement(1, "banded", 0f, 0f, ZIndex: authored)),
            SceneFixtures.Registry(("banded", _ => new Banded())));

        Assert.Equal(expected, scene.Entities[0].ZIndex);
    }

    private static EntitySpawner Spawns(int tag) => _ =>
    {
        Marker marker = new();
        marker.Add(Tag(tag));

        return marker;
    };

    // The offset is the renderer's identity in the frame: it draws its entity at the origin, so
    // the intent's X is the tag.
    private static SpriteRenderer Tag(int tag, int zIndex = 0) =>
        new(SceneFixtures.Frame(1, 1)) { Offset = new Vector2(tag, 0f), ZIndex = zIndex };

    private static int[] Order(SceneSimulation simulation)
    {
        ReadOnlySpan<SpriteIntent> sprites = simulation.View.Sprites;
        int[] tags = new int[sprites.Length];
        for (int index = 0; index < tags.Length; index++)
        {
            tags[index] = (int)sprites[index].Position.X;
        }

        return tags;
    }

    private sealed class Marker() : Entity(Vector2.Zero);

    private static void Emit(FrameView view, int tag)
    {
        Vector2 at = new(tag, 0f);
        view.Add(new SpriteIntent(
            SceneFixtures.Frame(1, 1),
            at,
            at,
            new Vector2(1f, 1f),
            FlipX: false,
            FlipY: false,
            ColorRgba.White));
    }

    /// <summary>Writes its own key, and optionally detaches a peer, from inside its own Draw.</summary>
    private sealed class Raising(int tag, int raisedTo, Renderer? detaches = null) : Renderer
    {
        public override void Draw(FrameView view)
        {
            // Idempotent: the setter ignores a write of the value it already holds, so this
            // raises once and does not re-invalidate on every later frame.
            ZIndex = raisedTo;
            detaches?.Entity?.Remove(detaches);
            Emit(view, tag);
        }
    }

    /// <summary>Lowers a peer's key, and optionally detaches another, from inside its own Draw.</summary>
    private sealed class Lowering(int tag, Renderer peer, int loweredTo, Renderer? detaches = null) : Renderer
    {
        public override void Draw(FrameView view)
        {
            peer.ZIndex = loweredTo;
            detaches?.Entity?.Remove(detaches);
            Emit(view, tag);
        }
    }

    /// <summary>An entity that bands itself, so a placement has a default to leave or to override.</summary>
    private sealed class Banded : Entity
    {
        internal const int Band = 12;

        internal Banded()
            : base(Vector2.Zero) => ZIndex = Band;
    }
}
