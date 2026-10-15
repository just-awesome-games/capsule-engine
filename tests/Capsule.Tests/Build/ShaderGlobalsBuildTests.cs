using System.Text;
using Capsule.Build.Shaders;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// A fragment declares the parameters a scene sets in its <c>SceneGlobals</c> block. They reach the
/// game's key marked global and the effect as a buffer of their own beside the material's.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class ShaderGlobalsBuildTests
{
    private const string Graded = """
        cbuffer SceneGlobals
        {
            float3 WorldAdd;
            float Fade;
        };
        float4 Glow;

        float4 Fragment(SpritePixel pixel)
        {
            float4 color = pixel.Texel * pixel.Tint + Glow;
            color.rgb = color.rgb * Fade + WorldAdd;
            return color;
        }
        """;

    [Fact]
    public void ABlockNamedSceneGlobals_ShipsItsParametersAsGlobal_InABufferBesideTheMaterials()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Shaders/graded.fx", Graded);

        workspace.Succeed();

        Assert.Contains(
            """new global::Capsule.Rendering.Shader("shaders/graded", new global::Capsule.Rendering.ShaderParameter("WorldAdd", global::Capsule.Rendering.ShaderParameterKind.Vector3, true), new global::Capsule.Rendering.ShaderParameter("Fade", global::Capsule.Rendering.ShaderParameterKind.Float, true), new global::Capsule.Rendering.ShaderParameter("Glow", global::Capsule.Rendering.ShaderParameterKind.Vector4));""",
            workspace.Generated,
            StringComparison.Ordinal);
        Assert.Contains("setting <c>Glow</c> and reading the scene globals <c>WorldAdd</c>, <c>Fade</c>.", workspace.Generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEffect_ReadsTheGlobalsFromTheirOwnUniformArray()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("graded.fx", ShaderTemplate.Compose(Graded, "graded.fx"));

        ShaderCompilation compiled = ShaderCompiler.Compile(ToolWorkspace.ShaderTools, "graded.fx");
        byte[] effect = Assert.IsType<byte[]>(compiled.Effect);
        string text = Encoding.ASCII.GetString(effect);

        Assert.Contains(MgfxWriter.GlobalsBuffer, text, StringComparison.Ordinal);
        Assert.DoesNotContain("type_SceneGlobals", text, StringComparison.Ordinal);
        Assert.DoesNotContain("type_Globals", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cbuffer Palette { float3 Tone; };", "float4(Tone, 1.0)", "declares the constant buffer 'type.Palette'. Declare a shader parameter outside any buffer, or in a cbuffer named SceneGlobals for one set on the scene.")]
    [InlineData("cbuffer SceneGlobals { float Tones[2]; };", "Tones[1]", "declares scene global 'Tones', which a scene cannot set. A scene global is a float, float2, float3 or float4, and no array.")]
    [InlineData("cbuffer SceneGlobals { float3 Tone; }; cbuffer SceneGlobals { float Fade; };", "float4(Tone, Fade)", "declares the cbuffer SceneGlobals twice. Declare it once, holding every scene global.")]
    public void ADeclarationTheSceneCannotCarry_FailsTheBuildNamingTheFix(string declaration, string read, string failure)
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Shaders/bad.fx", $$"""
            {{declaration}}

            float4 Fragment(SpritePixel pixel)
            {
                return pixel.Texel * {{read}};
            }
            """);

        Assert.Contains("Assets/Shaders/bad.fx: " + failure, workspace.Fail(), StringComparison.Ordinal);
    }
}
