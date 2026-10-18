using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

public sealed class MaterialTests
{
    private static readonly Shader Stone = new(
        "effects/stone",
        new ShaderParameter("Amount", ShaderParameterKind.Float),
        new ShaderParameter("Stain", ShaderParameterKind.Vector4));

    private static readonly Shader Graded = new(
        "effects/graded",
        new ShaderParameter("Amount", ShaderParameterKind.Float),
        new ShaderParameter("Scroll", ShaderParameterKind.Vector2),
        new ShaderParameter("Stain", ShaderParameterKind.Vector3),
        new ShaderParameter("Glow", ShaderParameterKind.Vector4),
        new ShaderParameter("Grain", ShaderParameterKind.Texture),
        new ShaderParameter("WorldAdd", ShaderParameterKind.Vector3, Global: true));

    // A misspelt or mistyped parameter throws where the material is written, naming the shader and
    // what it declares instead of drawing with a value that never arrives.
    [Theory]
    [InlineData("Amont", "Shader 'effects/stone' declares no parameter 'Amont'. It declares 'Amount' (float), 'Stain' (float4).")]
    [InlineData("Stain", "Shader 'effects/stone' declares 'Stain' as a float4. Pass it a Vector4 or a ColorRgba, or read it with GetVector4 or GetColor.")]
    public void Set_RefusesAnUnknownNameOrTheWrongKind(string name, string message)
    {
        Material material = new(Stone);

        ArgumentException refused = Assert.Throws<ArgumentException>(() => material.Set(name, 1f));

        Assert.StartsWith(message, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_ReadsBackWhatSetStored()
    {
        Material material = new(Graded);
        TextureHandle grain = new("grain", ".png");

        Assert.Equal(0f, material.GetFloat("Amount"));
        Assert.Equal(Vector3.Zero, material.GetVector3("Stain"));
        Assert.Equal(default, material.GetColor("Glow"));
        Assert.Equal(default, material.GetTexture("Grain"));

        material.Set("Amount", 0.5f);
        material.Set("Scroll", new Vector2(1f, 2f));
        material.Set("Stain", new Vector3(1f, 2f, 3f));
        material.Set("Glow", new ColorRgba(255, 128, 0, 64));
        material.Set("Grain", grain);

        Assert.Equal(0.5f, material.GetFloat("Amount"));
        Assert.Equal(new Vector2(1f, 2f), material.GetVector2("Scroll"));
        Assert.Equal(new Vector3(1f, 2f, 3f), material.GetVector3("Stain"));
        Assert.Equal(new ColorRgba(255, 128, 0, 64), material.GetColor("Glow"));
        Assert.Equal(new Vector4(1f, 128f / 255f, 0f, 64f / 255f), material.GetVector4("Glow"));
        Assert.Equal(grain, material.GetTexture("Grain"));
    }

    // GetColor rounds to the nearest byte and clamps each channel to 0 to 1. ShaderGlobals.GetColor shares
    // this conversion. It is covered here once.
    [Fact]
    public void GetColor_ClampsEachChannelAndRoundsToTheNearestByte()
    {
        Material material = new(Graded);

        material.Set("Glow", new Vector4(100.7f / 255f, 1.5f, -0.2f, 100.3f / 255f));

        Assert.Equal(new ColorRgba(101, 255, 0, 100), material.GetColor("Glow"));
    }

    [Fact]
    public void Get_OfAnotherKindThanTheShaderDeclares_ThrowsNamingTheFix()
    {
        Material material = new(Graded);

        ArgumentException refused = Assert.Throws<ArgumentException>(() => material.GetFloat("Glow"));

        Assert.StartsWith("Shader 'effects/graded' declares 'Glow' as a float4. Pass it a Vector4 or a ColorRgba, or read it with GetVector4 or GetColor.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGlobal_IsRefusedByASetAndAGetAlike_NamingTheScene()
    {
        Material material = new(Graded);
        const string Message = "Shader 'effects/graded' declares 'WorldAdd' in its SceneGlobals block. A material neither sets nor reads it. Use scene.ShaderGlobals.Set or the Get of its kind.";

        ArgumentException set = Assert.Throws<ArgumentException>(() => material.Set("WorldAdd", Vector3.One));
        ArgumentException read = Assert.Throws<ArgumentException>(() => material.GetVector3("WorldAdd"));

        Assert.StartsWith(Message, set.Message, StringComparison.Ordinal);
        Assert.StartsWith(Message, read.Message, StringComparison.Ordinal);
    }

    // Runs are recorded per layer where the stored sprite's material differs from the layer's last
    // run. An unchanged material costs no run and the other layer's sprites break nothing.
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
