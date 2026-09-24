using System.Reflection;
using System.Runtime.CompilerServices;
using Capsule.Build;
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
    private const string Out = "obj/capsule";

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

    private static readonly ShaderTools Tools = new(Metadata("CapsuleDxc"), Metadata("CapsuleSpirvCross"));

    [Fact]
    public void AShader_ShipsCompiled_WithItsParameterTableInTheGeneratedKey()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("Assets/Shaders/Effects/Stone.fx", Stone);

        StringWriter error = new();
        int exitCode = Run(workspace, error, "shaders|Effects/Stone|.fx|Assets/Shaders/Effects/Stone.fx");

        Assert.True(exitCode == 0, error.ToString());
        Assert.Contains(
            """new global::Capsule.Rendering.Shader("effects/stone", new global::Capsule.Rendering.ShaderParameter("Amount", global::Capsule.Rendering.ShaderParameterKind.Float), new global::Capsule.Rendering.ShaderParameter("Scroll", global::Capsule.Rendering.ShaderParameterKind.Vector2), new global::Capsule.Rendering.ShaderParameter("Stain", global::Capsule.Rendering.ShaderParameterKind.Vector3), new global::Capsule.Rendering.ShaderParameter("Glow", global::Capsule.Rendering.ShaderParameterKind.Vector4), new global::Capsule.Rendering.ShaderParameter("Grain", global::Capsule.Rendering.ShaderParameterKind.Texture));""",
            File.ReadAllText(Out + "/CapsuleAssets.Shaders.g.cs"),
            StringComparison.Ordinal);

        string shipped = Assert.Single(File.ReadAllLines(Out + "/shipped-assets.txt"));
        Assert.StartsWith("assets/shaders/effects/stone.mgfx|", shipped, StringComparison.Ordinal);
        Assert.True(File.Exists(shipped.Split('|')[1]));
    }

    [Fact]
    public void AShaderThatDoesNotCompile_FailsAtTheGamesFileAndLine()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("Assets/Shaders/broken.fx", "float4 Fragment(SpritePixel pixel)\n{\n    return pixel.Texel * Glow;\n}\n");

        StringWriter error = new();
        int exitCode = Run(workspace, error, "shaders|broken|.fx|Assets/Shaders/broken.fx");

        Assert.Equal(1, exitCode);
        Assert.Contains("Assets/Shaders/broken.fx(3,26): error : use of undeclared identifier 'Glow'", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Out + "/build.stamp"));
    }

    // The container is read back by MonoGame's own effect reader, run without a device, and every
    // parameter lands where the pixel stage's constant buffer and samplers expect it.
    [Fact]
    public void TheEffectContainer_ReadsBackThroughTheSubstratesReader()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("stone.fx", ShaderTemplate.Compose(Stone, "stone.fx"));

        ShaderCompilation compiled = ShaderCompiler.Compile(Tools, "stone.fx");
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
            ["MatrixTransform", "SpriteTexture", "Amount", "Scroll", "Stain", "Glow", "Grain"],
            parameters.Select(static parameter => (string)parameter.GetType().GetProperty("Name")!.GetValue(parameter)!));
    }

    private static int Run(SceneDocumentFixtures.Workspace workspace, TextWriter error, params string[] manifest)
    {
        string requests = workspace.Write(
            "requests.txt",
            string.Join('\n', [$"shader-dxc|{Tools.Dxc}", $"shader-spirv-cross|{Tools.SpirvCross}", .. manifest]));

        return BuildRun.Run(requests, Out, TextWriter.Null, error);
    }

    private static string Metadata(string key) =>
        typeof(ShaderBuildTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == key).Value!;
}
