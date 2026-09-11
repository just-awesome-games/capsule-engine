using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;
using Capsule.Tests.Rendering;

namespace Capsule.Tests.Scenes;

public sealed class LabelTests
{
    [Fact]
    public void ALabel_PreloadsEveryPageItsFontIsCutFrom()
    {
        Scene scene = new();
        scene.Add(Prompt("A"));

        Assert.Equal([FontFixtures.Page], scene.CollectAssetPreloads().Textures);
    }

    [Fact]
    public void ALabel_DrawsItsRunFromTheEntityPositionPlusItsOffset()
    {
        Holder entity = Prompt("AB");
        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        ReadOnlySpan<SpriteIntent> drawn = simulation.View.Sprites;

        Assert.Equal(2, drawn.Length);

        // The entity sits at (4, 5) and the label offsets by (1, 1); 'A' then carries its own
        // one-pixel bearing.
        Assert.Equal(new Vector2(6f, 8f), drawn[0].Position);
        Assert.Equal(new Vector2(8f, 8f), drawn[1].Position);
    }

    private static Holder Prompt(string text) =>
        new(new Label(FontFixtures.Font(), text) { Offset = Vector2.One });

    private sealed class Holder : Entity
    {
        internal Holder(Label label)
            : base(new Vector2(4f, 5f))
        {
            Add(label);
        }
    }
}
