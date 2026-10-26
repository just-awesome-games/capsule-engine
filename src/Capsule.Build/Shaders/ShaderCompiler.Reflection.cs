using System.Text.Json;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

// The compile whose reflection is the parameter table every format shares. It compiles the template
// around one sampler, and names the textures the bound compile pairs samplers with.
internal static partial class ShaderCompiler
{
    // Compiles the composed source at basePath + ".fx" to SPIR-V and reflects its parameters. DXC reports
    // the game's errors here, against the lines the game wrote.
    private static FragmentReflection ReflectFragment(ShaderTools tools, string basePath, List<ShaderDiagnostic> diagnostics)
    {
        string directory = Path.GetDirectoryName(basePath)!;
        string source = Path.GetFileName(basePath) + ".fx";
        string spirv = Path.GetFileName(basePath) + ".spv";

        (int exit, string output, string errors) = Run(Binary(tools, "dxc"), directory, "-spirv", "-T", "ps_6_0", "-E", ShaderTemplate.EntryPoint, "-Fo", spirv, source);
        diagnostics.AddRange(Diagnostics(output + '\n' + errors));
        if (exit != 0 || !File.Exists(Path.Combine(directory, spirv)))
        {
            return FragmentReflection.Failed(diagnostics.Any(static diagnostic => !diagnostic.Warning) ? null : "the shader compiler failed: " + Flatten(output + errors));
        }

        (exit, output, errors) = Run(Binary(tools, "spirv-cross"), directory, spirv, "--reflect");
        if (exit != 0)
        {
            return FragmentReflection.Failed("the shader cross-compiler could not reflect it: " + Flatten(output + errors));
        }

        if (Reflect(output, out List<ShaderBinding> constants, out int bufferSize, out int globalsSize, out List<string> textures) is { } refused)
        {
            return FragmentReflection.Failed(refused);
        }

        return new FragmentReflection(true, constants, textures, bufferSize > 0, globalsSize > 0, null);
    }

    // The parameter table from SPIRV-Cross's reflection: the members of the global block and of the
    // scene globals block with their offsets in their own block, and every texture beyond the
    // sprite's. Returns why a parameter cannot be set, or null.
    private static string? Reflect(string json, out List<ShaderBinding> constants, out int bufferSize, out int globalsSize, out List<string> textures)
    {
        constants = [];
        textures = [];
        bufferSize = 0;
        globalsSize = 0;

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("ubos", out JsonElement blocks))
        {
            foreach (JsonElement block in blocks.EnumerateArray())
            {
                string blockName = block.GetProperty("name").GetString()!;
                bool scene = blockName == "type." + ShaderTemplate.SceneGlobalsBlock;
                if (!scene && blockName != "type.$Globals")
                {
                    return $"declares the constant buffer '{blockName}'. Declare a shader parameter outside any buffer, or in a cbuffer named {ShaderTemplate.SceneGlobalsBlock} for one set on the scene.";
                }

                if (scene && globalsSize > 0)
                {
                    return $"declares the cbuffer {ShaderTemplate.SceneGlobalsBlock} twice. Declare it once, holding every scene global.";
                }

                int size = (block.GetProperty("block_size").GetInt32() + 15) / 16 * 16;
                if (scene)
                {
                    globalsSize = size;
                }
                else
                {
                    bufferSize = size;
                }

                JsonElement type = root.GetProperty("types").GetProperty(block.GetProperty("type").GetString()!);

                foreach (JsonElement member in type.GetProperty("members").EnumerateArray())
                {
                    string name = member.GetProperty("name").GetString()!;
                    if (Reserved(name))
                    {
                        return $"declares parameter '{name}', a name the engine reserves. Rename it without the {ShaderTemplate.ReservedPrefix} prefix.";
                    }

                    ShaderParameterKind? kind = member.TryGetProperty("array", out _)
                        ? null
                        : member.GetProperty("type").GetString() switch
                        {
                            "float" => ShaderParameterKind.Float,
                            "vec2" => ShaderParameterKind.Vector2,
                            "vec3" => ShaderParameterKind.Vector3,
                            "vec4" => ShaderParameterKind.Vector4,
                            _ => null,
                        };

                    if (kind is not { } settable)
                    {
                        return scene
                            ? $"declares scene global '{name}', which a scene cannot set. A scene global is a float, float2, float3 or float4, and no array."
                            : $"declares parameter '{name}', which a Material cannot set. A shader parameter is a float, float2, float3, float4 or Texture2D, and no array.";
                    }

                    constants.Add(new ShaderBinding(name, settable, scene, member.GetProperty("offset").GetInt32()));
                }
            }
        }

        if (root.TryGetProperty("separate_images", out JsonElement images))
        {
            foreach (JsonElement image in images.EnumerateArray())
            {
                string name = image.GetProperty("name").GetString()!;
                if (Reserved(name))
                {
                    return $"declares texture '{name}', a name the engine reserves. Rename it without the {ShaderTemplate.ReservedPrefix} prefix.";
                }

                if (image.GetProperty("type").GetString() != "texture2D")
                {
                    return $"declares texture '{name}' of a kind a Material cannot set. A texture parameter is a Texture2D.";
                }

                if (name != ShaderTemplate.SpriteTexture)
                {
                    textures.Add(name);
                }
            }
        }

        if (root.TryGetProperty("separate_samplers", out JsonElement samplers))
        {
            foreach (JsonElement sampler in samplers.EnumerateArray())
            {
                if (sampler.GetProperty("name").GetString() != ShaderTemplate.Sampler)
                {
                    return "declares a sampler of its own. Read a texture with Sample(texture, uv), which samples as the sprite does.";
                }
            }
        }

        return null;
    }

    // Whether a game parameter's name is the engine's: any name the template or the host binds carries
    // the reserved prefix.
    private static bool Reserved(string name) => name.StartsWith(ShaderTemplate.ReservedPrefix, StringComparison.Ordinal);

    // A fragment's parameter table, or why its compile failed: the constants at their offsets in their
    // buffers and the textures beyond the sprite's in slot order.
    private sealed record FragmentReflection(bool Compiled, List<ShaderBinding> Constants, List<string> Textures, bool HasMaterialBuffer, bool HasGlobalsBuffer, string? Failure)
    {
        internal static FragmentReflection Failed(string? failure) => new(false, [], [], false, false, failure);
    }
}
