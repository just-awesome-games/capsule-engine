using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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
    // A stone-statue look: the texel's luminance, mixed in by Amount and stained by a texture tiled at
    // the sprite's texel scale, with a parameter of every kind a Material sets.
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
            float2 uv = pixel.UV * pixel.TextureSize / TextureSize(Grain) + Scroll;
            float3 stone = grey * Stain * Sample(Grain, uv).r + Glow.rgb;
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

    // The tools' package folder is named for its version. A shader built by one version is rebuilt by
    // the next, never reused.
    [Fact]
    public void AShaderToolUpgrade_MissesTheCache()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Shaders/stone.fx", Stone);
        workspace.Succeed();
        workspace.Succeed();
        Assert.Contains("shaders: 1 reused", workspace.Output, StringComparison.Ordinal);

        workspace.Tools = new ShaderTools(Path.Combine(Path.GetDirectoryName(ToolWorkspace.ShaderTools.Package)!, "0.0.0"));
        workspace.Run();

        Assert.DoesNotContain("shaders: 1 reused", workspace.Output, StringComparison.Ordinal);
    }

    // The container is read back by MonoGame's own effect reader, run without a device, and every
    // parameter lands where the pixel stage's constant buffer and samplers expect it. Each texture size
    // the fragment reads is a parameter the host sets, and the stage names no sampler it never binds.
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
            ["MatrixTransform", "SpriteTexture", "CapsuleCoverage", "Amount", "Scroll", "Stain", "Glow", "CapsuleTextureSize0", "CapsuleTextureSize1", "Grain"],
            parameters.Select(static parameter => (string)parameter.GetType().GetProperty("Name")!.GetValue(parameter)!));
        Assert.DoesNotContain("SPIRV_Cross", Encoding.ASCII.GetString(effect), StringComparison.Ordinal);
    }

    // A game name that only contains a GLSL built-in's name is the game's own. This table, written
    // and indexed as the fragment runs, reaches the GLSL under its own name.
    [Fact]
    public void AGameNameContainingABuiltInsName_Builds()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("table.fx", ShaderTemplate.Compose(
            """
            static float textureSizeWeights[4] = { 0.25, 0.5, 0.75, 1.0 };

            float4 Fragment(SpritePixel pixel)
            {
                textureSizeWeights[3] = pixel.Tint.a;
                return pixel.Texel * textureSizeWeights[(int)(pixel.UV.x * 3.0)];
            }
            """,
            "table.fx"));

        Assert.Null(ShaderCompiler.Compile(ToolWorkspace.ShaderTools, "table.fx").Failure);
    }

    // A fragment that does not compile fails at the game's file and line. A read OpenGL 2.1 lacks fails
    // naming what to write instead, rather than failing or reading a wrong value when the game draws.
    // The host binds the engine's parameters by name, and a game parameter carrying its prefix fails
    // rather than being bound as one.
    [Theory]
    [InlineData("float4 Fragment(SpritePixel pixel)\n{\n    return pixel.Texel * Glow;\n}\n", "(3,26): error : use of undeclared identifier 'Glow'")]
    [InlineData("Texture2D Grain;\nfloat4 Fragment(SpritePixel pixel) { return Grain.Load(int3(2, 3, 0)); }", ": reads a texel with Load or an index, which OpenGL 2.1 lacks. Read it with Sample(texture, (texel + 0.5) / TextureSize(texture)).")]
    [InlineData("Texture2D Grain;\nfloat4 Fragment(SpritePixel pixel) { uint w, h; Grain.GetDimensions(w, h); return Sample(Grain, pixel.UV) * w; }", ": reads a texture's size with GetDimensions, which OpenGL 2.1 lacks. Read it with TextureSize(texture).")]
    [InlineData("float4 Fragment(SpritePixel pixel) { return pixel.Texel * round(pixel.UV.x * 4.0); }", ": needs the GLSL extension GL_EXT_gpu_shader4, which OpenGL 2.1 lacks.")]
    [InlineData("float2 CapsuleTextureSize1;\nTexture2D Grain;\nfloat4 Fragment(SpritePixel pixel) { return Sample(Grain, pixel.UV / CapsuleTextureSize1) * pixel.Texel; }", ": declares parameter 'CapsuleTextureSize1', a name the engine reserves. Rename it without the Capsule prefix.")]
    [InlineData("Texture2D CapsuleGrain;\nTexture2D Grain;\nfloat4 Fragment(SpritePixel pixel) { return Sample(Grain, pixel.UV + Sample(CapsuleGrain, pixel.UV).rg) * pixel.Texel; }", ": declares texture 'CapsuleGrain', a name the engine reserves. Rename it without the Capsule prefix.")]
    public void AShaderTheBuildCannotShip_FailsAtTheGamesFileNamingTheFix(string source, string failure)
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Shaders/refused.fx", source);

        Assert.Contains("Assets/Shaders/refused.fx" + failure, workspace.Fail(), StringComparison.Ordinal);
    }

    // The runtime embeds the committed sprite shader. A game drawing only with it needs no shader tools.
    [Fact]
    public void TheEnginesSpriteShader_IsCommittedAsTheTemplateCompilesIt()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("capsule-sprite.fx", ShaderTemplate.ComposeDefault());

        byte[] effect = Assert.IsType<byte[]>(ShaderCompiler.Compile(ToolWorkspace.ShaderTools, "capsule-sprite.fx").Effect);

        string committed = Path.Combine(ToolWorkspace.Metadata("CapsuleCheckout"), "src", "Capsule.Runtime", "Rendering", "Shaders", "sprite.mgfx");
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
