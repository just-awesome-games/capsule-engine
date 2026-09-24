using System.Numerics;
using Capsule.Rendering;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

public sealed class MaterialTests
{
    private static readonly Shader Stone = new(
        "effects/stone",
        new ShaderParameter("Amount", ShaderParameterKind.Float),
        new ShaderParameter("Stain", ShaderParameterKind.Vector4));

    // A misspelt or mistyped parameter throws where the material is written, naming the shader and
    // what it declares instead of drawing with a value that never arrives.
    [Theory]
    [InlineData("Amont", "Shader 'effects/stone' declares no parameter 'Amont'. It declares 'Amount' (float), 'Stain' (float4).")]
    [InlineData("Stain", "Shader 'effects/stone' declares 'Stain' as a float4. Pass it a Vector4 or a ColorRgba.")]
    public void Set_RefusesAnUnknownNameOrTheWrongKind(string name, string message)
    {
        Material material = new(Stone);

        ArgumentException refused = Assert.Throws<ArgumentException>(() => material.Set(name, 1f));

        Assert.StartsWith(message, refused.Message, StringComparison.Ordinal);
    }

    // Runs are recorded per layer where the stored sprite's material differs from the layer's last
    // run, so an unchanged material costs no run and the other layer's sprites break nothing.
    [Fact]
    public void MaterialRuns_OpenOnlyWhereALayersMaterialChanges()
    {
        Material stone = new(Stone);
        Material moss = new(Stone);
        FrameView view = new();

        Store(view, null, RenderSpace.World);
        Store(view, stone, RenderSpace.World);
        Store(view, stone, RenderSpace.Screen);
        Store(view, stone, RenderSpace.World);
        Store(view, moss, RenderSpace.World);
        Store(view, null, RenderSpace.World);
        Store(view, null, RenderSpace.Screen);

        Assert.Equal([new MaterialRun(1, stone), new MaterialRun(3, moss), new MaterialRun(4, null)], view.MaterialRuns.ToArray());
        Assert.Equal([new MaterialRun(0, stone), new MaterialRun(1, null)], view.ScreenMaterialRuns.ToArray());
    }

    private static void Store(FrameView view, Material? material, RenderSpace space)
    {
        view.Material = material;
        view.Add(new SpriteIntent(SceneFixtures.Frame(1, 1), Vector2.Zero, Vector2.Zero, 0f, 0f, Vector2.One, false, false, ColorRgba.White), space);
    }
}
