using System.Reflection;
using System.Runtime.CompilerServices;
using Capsule.Build.Shaders;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// A game's fragment reaches the game as a compiled effect and a generated key, through the native
/// shader tools every desktop host restores.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class ShaderBuildTests
{
    // A stone-statue look: the texel's luminance, mixed in by Amount and stained by a texture, with a
    // parameter of every kind a Material sets.
    private const string Stone = """
        float Amount;
        float2 Scroll;
        float3 Stain;
        float4 Glow;
        Texture2D Grain;

        float4 Fragment(SpritePixel pixel)
        {
            float4 color = pixel.Texel * pixel.Tint;
            float grey = dot(color.rgb, float3(0.299, 0.587, 0.114));
            float3 stone = grey * Stain * Sample(Grain, pixel.UV + Scroll).r + Glow.rgb;
            color.rgb = lerp(color.rgb, stone, Amount);
            return color;
        }
        """;

    [Fact]
    public void AShader_ShipsCompiled_WithItsParameterTableInTheGeneratedKey()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Shaders/Effects/Stone.fx", Stone);

        workspace.Succeed();

        Assert.Contains(
            """new global::Capsule.Rendering.Shader("shaders/effects/stone", new global::Capsule.Rendering.ShaderParameter("Amount", global::Capsule.Rendering.ShaderParameterKind.Float), new global::Capsule.Rendering.ShaderParameter("Scroll", global::Capsule.Rendering.ShaderParameterKind.Vector2), new global::Capsule.Rendering.ShaderParameter("Stain", global::Capsule.Rendering.ShaderParameterKind.Vector3), new global::Capsule.Rendering.ShaderParameter("Glow", global::Capsule.Rendering.ShaderParameterKind.Vector4), new global::Capsule.Rendering.ShaderParameter("Grain", global::Capsule.Rendering.ShaderParameterKind.Texture));""",
            workspace.Generated,
            StringComparison.Ordinal);
        Assert.Equal(["shaders/effects/stone.mgfx"], workspace.Shipped);
    }

    [Fact]
    public void AShaderThatDoesNotCompile_FailsAtTheGamesFileAndLine()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Shaders/broken.fx", "float4 Fragment(SpritePixel pixel)\n{\n    return pixel.Texel * Glow;\n}\n");

        Assert.Contains(
            "Assets/Shaders/broken.fx(3,26): error : use of undeclared identifier 'Glow'",
            workspace.Fail(),
            StringComparison.Ordinal);
    }

    // The container is read back by MonoGame's own effect reader, run without a device, and every
    // parameter lands where the pixel stage's constant buffer and samplers expect it.
    [Fact]
    public void TheEffectContainer_ReadsBackThroughTheSubstratesReader()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("stone.fx", ShaderTemplate.Compose(Stone, "stone.fx"));

        ShaderCompilation compiled = ShaderCompiler.Compile(ToolWorkspace.ShaderTools, "stone.fx");
        byte[] effect = Assert.IsType<byte[]>(compiled.Effect);

        Type type = Type.GetType("Microsoft.Xna.Framework.Graphics.Effect, MonoGame.Framework", throwOnError: true)!;
        object reading = RuntimeHelpers.GetUninitializedObject(type);
        object header = Activator.CreateInstance(type.GetNestedType("MGFXHeader", BindingFlags.NonPublic)!)!;
        header.GetType().GetField("Version")!.SetValue(header, (int)MgfxWriter.Version);

        using BinaryReader reader = new(new MemoryStream(effect, 10, effect.Length - 10));
        type.GetMethod("ReadEffect", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(reading, [header, reader]);

        Assert.Equal("MGFX"u8.ToArray(), reader.ReadBytes(4));
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);

        IEnumerable<object> parameters = (IEnumerable<object>)type.GetProperty("Parameters")!.GetValue(reading)!;
        Assert.Equal(
            ["MatrixTransform", "SpriteTexture", "CapsuleCoverage", "Amount", "Scroll", "Stain", "Glow", "Grain"],
            parameters.Select(static parameter => (string)parameter.GetType().GetProperty("Name")!.GetValue(parameter)!));
    }

    // The runtime embeds the committed sprite shader. A game drawing only with it needs no shader tools.
    [Fact]
    public void TheEnginesSpriteShader_IsCommittedAsTheTemplateCompilesIt()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("capsule-sprite.fx", ShaderTemplate.ComposeDefault());

        byte[] effect = Assert.IsType<byte[]>(ShaderCompiler.Compile(ToolWorkspace.ShaderTools, "capsule-sprite.fx").Effect);

        string committed = ToolWorkspace.Metadata("CapsuleSpriteShader");
        if (Environment.GetEnvironmentVariable("CAPSULE_UPDATE_ENGINE_SHADER") == "1")
        {
            File.WriteAllBytes(committed, effect);
        }

        Assert.True(
            File.ReadAllBytes(committed).AsSpan().SequenceEqual(effect),
            $"'{committed}' is not what the default template compiles to. The template, the shader compiler code or a shader tool version changed. "
                + "Rerunning this test with CAPSULE_UPDATE_ENGINE_SHADER=1 rewrites the file. Review it and commit it.");
    }
}
