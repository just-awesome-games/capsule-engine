using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Capsule.Build.Shaders;
using Capsule.Rendering;
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
        Assert.Equal(
            ["shaders/effects/stone.dxil.shader", "shaders/effects/stone.glsl.shader", "shaders/effects/stone.msl.shader", "shaders/effects/stone.spirv.shader"],
            workspace.Shipped);
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

    // A texture parameter, a material value and a scene global, each read.
    private const string Grained = """
        Texture2D Grain;
        float Amount;

        cbuffer SceneGlobals
        {
            float3 WorldAdd;
        };

        float4 Fragment(SpritePixel pixel)
        {
            float4 color = pixel.Texel * pixel.Tint * Sample(Grain, pixel.UV * Amount);
            color.rgb += WorldAdd * color.a;
            return color;
        }
        """;

    // Every format binds as SDL's GPU API reads it: each texture with a sampler at its own slot of the
    // fragment's resources, the sprite's at 0, then the material's uniforms in buffer 0 and the scene
    // globals in buffer 1. The DXIL is signed, without which Direct3D 12 refuses it.
    [Fact]
    public void EveryFormat_BindsInSdlsLayout()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Shaders/grained.fx", Grained);
        workspace.Succeed();

        Dictionary<string, ShaderContainer> shaders = ShaderCompiler.Formats.ToDictionary(
            static format => format,
            static format => ShaderContainer.Read(File.ReadAllBytes($"{ToolWorkspace.Out}/assets/shaders/grained.{format}.shader"), format));
        foreach (ShaderContainer shader in shaders.Values)
        {
            Assert.Equal((ShaderStage.Fragment, 2, 2), (shader.Stage, shader.Samplers, shader.UniformBuffers));
            Assert.Equal(
                [new("Amount", ShaderParameterKind.Float, false, 0), new("WorldAdd", ShaderParameterKind.Vector3, true, 0), new ShaderBinding("Grain", ShaderParameterKind.Texture, false, 1)],
                shader.Parameters);
        }

        string reflection = Tool("spirv-cross", Write("grained.spv", shaders[ShaderCompiler.Spirv].Code), "--reflect");
        using JsonDocument spirv = JsonDocument.Parse(reflection);
        Assert.Equal(
            ["CapsuleSampler_Grain 2:1", "CapsuleSampler_SpriteTexture 2:0", "Grain 2:1", "SpriteTexture 2:0", "type.$Globals 3:0", "type.SceneGlobals 3:1"],
            ((string[])["separate_images", "separate_samplers", "ubos"])
                .SelectMany(kind => spirv.RootElement.GetProperty(kind).EnumerateArray())
                .Select(static resource => $"{resource.GetProperty("name").GetString()} {resource.GetProperty("set").GetInt32()}:{resource.GetProperty("binding").GetInt32()}")
                .Order(StringComparer.Ordinal));

        string dxil = Tool("dxc", "-dumpbin", Write("grained.dxil", shaders[ShaderCompiler.Dxil].Code));
        foreach (string register in (string[])["cb0,space3", "cb1,space3", "s0,space2", "s1,space2", "t0,space2", "t1,space2"])
        {
            Assert.Contains(register, dxil, StringComparison.Ordinal);
        }

        Assert.True(ShaderCompiler.Signed(shaders[ShaderCompiler.Dxil].Code));

        string msl = Encoding.UTF8.GetString(shaders[ShaderCompiler.Msl].Code);
        foreach (string binding in (string[])["Grain [[texture(1)]]", "CapsuleSampler_Grain [[sampler(1)]]", "[[buffer(0)]]", "[[buffer(1)]]"])
        {
            Assert.Contains(binding, msl, StringComparison.Ordinal);
        }
    }

    // A DXIL container's hash follows its four-byte magic. All zero means no validator signed it.
    [Fact]
    public void DxilWithAZeroHash_IsUnsigned() =>
        Assert.False(ShaderCompiler.Signed([.. "DXBC"u8, .. new byte[16], 1, 0, 0, 0]));

    // A platform module's format compiles every stage through its tool, handed the stage as HLSL in
    // SDL's binding layout, and ships beside the engine's formats. The engine's own sprite stages ship
    // in it too, since the engine commits them only in its own formats. This tool ships the HLSL.
    [Fact]
    public void AFurtherFormat_CompilesEveryStageThroughItsTool()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Shaders/grained.fx", Grained);
        string tool = Path.GetFullPath(OperatingSystem.IsWindows()
            ? workspace.Write("tool.cmd", "@echo off\r\nshift\r\nshift\r\nshift\r\nshift\r\nshift\r\ncopy /y %1 %5 >nul\r\n")
            : workspace.Write("tool.sh", "#!/bin/sh\ncp \"$6\" \"${10}\"\n"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tool, File.GetUnixFileMode(tool) | UnixFileMode.UserExecute);
        }

        workspace.Tools = ToolWorkspace.ShaderTools with { Further = [new ShaderFormatTool("console", tool)] };
        workspace.Succeed();

        Assert.Subset(
            new HashSet<string>(workspace.Shipped),
            new HashSet<string>(["shaders/grained.console.shader", "capsule/sprite.console.shader", "capsule/vertex.console.shader"]));
        ShaderContainer grained = ShaderContainer.Read(File.ReadAllBytes($"{ToolWorkspace.Out}/assets/shaders/grained.console.shader"), "grained");
        Assert.Contains("Texture2D<float4> Grain : register(t1, space2);", Encoding.UTF8.GetString(grained.Code), StringComparison.Ordinal);
        Assert.Equal(ShaderStage.Vertex, ShaderContainer.Read(File.ReadAllBytes($"{ToolWorkspace.Out}/assets/capsule/vertex.console.shader"), "vertex").Stage);
    }

    // A shader tool's output, run on a file in the working directory.
    private static string Tool(string name, params string[] arguments)
    {
        ProcessStartInfo start = new(ShaderCompiler.Binary(ToolWorkspace.ShaderTools, name)) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }

    private static string Write(string name, byte[] bytes)
    {
        File.WriteAllBytes(name, bytes);

        return name;
    }

    // The container is read back by MonoGame's own effect reader, run without a device, and every
    // parameter lands where the pixel stage's constant buffer and samplers expect it. Each texture size
    // the fragment reads is a parameter the host sets, and the stage names no sampler it never binds.
    [Fact]
    public void TheEffectContainer_ReadsBackThroughTheSubstratesReader()
    {
        using SceneDocumentFixtures.Workspace workspace = new();

        ShaderCompilation compiled = ShaderCompiler.CompileFragment(ToolWorkspace.ShaderTools, "stone/stone", Stone, "stone.fx");
        byte[] effect = Assert.Single(Assert.IsType<ShaderContainer[]>(compiled.Shaders), static shader => shader.Format == ShaderCompiler.Glsl).Code;

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
        ShaderCompilation compiled = ShaderCompiler.CompileFragment(
            ToolWorkspace.ShaderTools,
            "table/table",
            """
            static float textureSizeWeights[4] = { 0.25, 0.5, 0.75, 1.0 };

            float4 Fragment(SpritePixel pixel)
            {
                textureSizeWeights[3] = pixel.Tint.a;
                return pixel.Texel * textureSizeWeights[(int)(pixel.UV.x * 3.0)];
            }
            """,
            "table.fx");

        Assert.Null(compiled.Failure);
        Assert.NotNull(compiled.Shaders);
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
    [InlineData("Texture2D Grain;\nfloat4 Through(Texture2D source, float2 uv) { return Sample(source, uv); }\nfloat4 Fragment(SpritePixel pixel) { return Through(Grain, pixel.UV) * pixel.Texel; }", "(2,54): error : samples a texture that is not a texture parameter. Call Sample(texture, uv) with the parameter itself, not a function argument holding it.")]
    public void AShaderTheBuildCannotShip_FailsAtTheGamesFileNamingTheFix(string source, string failure)
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Shaders/refused.fx", source);

        Assert.Contains("Assets/Shaders/refused.fx" + failure, workspace.Fail(), StringComparison.Ordinal);
    }

    // The runtime embeds the committed sprite shader, its fragment and vertex stage in each of the
    // engine's formats. A game drawing only with it needs no shader tools.
    [Fact]
    public void TheEnginesSpriteShader_IsCommittedAsTheTemplateCompilesIt()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        ShaderCompilation sprite = ShaderCompiler.CompileFragment(ToolWorkspace.ShaderTools, "sprite/capsule-sprite", ShaderTemplate.DefaultFragment, ShaderTemplate.DefaultFragmentFile);
        ShaderCompilation vertex = ShaderCompiler.CompileVertex(ToolWorkspace.ShaderTools, "vertex/capsule-vertex");
        Dictionary<string, byte[]> compiled = new(StringComparer.Ordinal);
        foreach ((string stage, ShaderCompilation compilation) in (ReadOnlySpan<(string, ShaderCompilation)>)[("sprite", sprite), ("vertex", vertex)])
        {
            foreach (ShaderContainer shader in Assert.IsType<ShaderContainer[]>(compilation.Shaders))
            {
                compiled.Add(stage + ShaderContainer.Suffix(shader.Format), shader.Write());
            }
        }

        string committed = Path.Combine(ToolWorkspace.Metadata("CapsuleCheckout"), "src", "Capsule.Runtime", "Rendering", "Shaders");
        if (Environment.GetEnvironmentVariable("CAPSULE_UPDATE_ENGINE_SHADER") == "1")
        {
            foreach (string stale in Directory.EnumerateFiles(committed, "*" + ShaderContainer.Extension))
            {
                File.Delete(stale);
            }

            foreach ((string name, byte[] bytes) in compiled)
            {
                File.WriteAllBytes(Path.Combine(committed, name), bytes);
            }
        }

        const string Rerun = "The template, the shader compiler code or a shader tool version changed. Rerunning this test with CAPSULE_UPDATE_ENGINE_SHADER=1 rewrites the files. Review them and commit them.";
        Assert.True(
            compiled.Keys.Order(StringComparer.Ordinal).SequenceEqual(Directory.EnumerateFiles(committed, "*" + ShaderContainer.Extension).Select(Path.GetFileName).Order(StringComparer.Ordinal)),
            $"'{committed}' does not hold one file per stage and format. {Rerun}");
        foreach ((string name, byte[] bytes) in compiled)
        {
            Assert.True(File.ReadAllBytes(Path.Combine(committed, name)).AsSpan().SequenceEqual(bytes), $"'{name}' is not what the template compiles to. {Rerun}");
        }
    }
}
