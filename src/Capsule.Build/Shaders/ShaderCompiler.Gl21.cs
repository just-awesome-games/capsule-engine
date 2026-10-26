using System.Globalization;
using System.Text.RegularExpressions;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

// The OpenGL 2.1 stage the desktop host draws with until it leaves MonoGame. It compiles the template
// around one sampler, and its reflection is the parameter table every format shares.
internal static partial class ShaderCompiler
{
    // The GLSL the desktop host's OpenGL 2.1 context accepts.
    private const string GlslVersion = "120";

    private const string CombinedPrefix = "SPIRV_Cross_Combined";

    // The parameter block SPIRV-Cross flattens the global constants into.
    private const string GlobalBlock = "type_Globals";

    // The block a fragment declares its scene globals in, as SPIRV-Cross flattens it.
    private const string SceneGlobalsBlock = "type_" + ShaderTemplate.SceneGlobalsBlock;

    // The effect for the composed source at basePath + ".fx" and its reflection, or why it failed. DXC
    // compiles the HLSL to SPIR-V, SPIRV-Cross reflects its parameters and writes it as GLSL, and
    // MgfxWriter packs it with the engine's vertex stage.
    private static Gl21Stage CompileGl21(ShaderTools tools, string basePath, List<ShaderDiagnostic> diagnostics)
    {
        string directory = Path.GetDirectoryName(basePath)!;
        string source = Path.GetFileName(basePath) + ".fx";
        string spirv = Path.GetFileName(basePath) + ".spv";

        (int exit, string output, string errors) = Run(Binary(tools, "dxc"), directory, "-spirv", "-T", "ps_6_0", "-E", ShaderTemplate.EntryPoint, "-Fo", spirv, source);
        diagnostics.AddRange(Diagnostics(output + '\n' + errors));
        if (exit != 0 || !File.Exists(Path.Combine(directory, spirv)))
        {
            return Gl21Stage.Failed(diagnostics.Any(static diagnostic => !diagnostic.Warning) ? null : "the shader compiler failed: " + Flatten(output + errors));
        }

        string crossCompiler = Binary(tools, "spirv-cross");
        (exit, output, errors) = Run(crossCompiler, directory, spirv, "--reflect");
        if (exit != 0)
        {
            return Gl21Stage.Failed("the shader cross-compiler could not reflect it: " + Flatten(output + errors));
        }

        if (Reflect(output, out List<ShaderBinding> constants, out int bufferSize, out int globalsSize, out List<string> textures) is { } refused)
        {
            return Gl21Stage.Failed(refused);
        }

        List<string> arguments = [spirv, "--version", GlslVersion, "--no-es", "--no-420pack-extension", "--flatten-ubo"];
        for (int location = 0; location < MgfxWriter.Varyings.Length; location++)
        {
            arguments.AddRange(["--rename-interface-variable", "in", location.ToString(CultureInfo.InvariantCulture), MgfxWriter.Varyings[location]]);
        }

        (exit, output, errors) = Run(crossCompiler, directory, [.. arguments]);
        if (exit != 0)
        {
            return Gl21Stage.Failed("the shader cross-compiler could not write it as GLSL: " + Flatten(output + errors));
        }

        string glsl = output.ReplaceLineEndings("\n");
        int pixelBufferSize = bufferSize;
        if (AdaptForGl21(ref glsl, textures, ref pixelBufferSize, out List<ShaderBinding> sizes) is { } unsupported)
        {
            return Gl21Stage.Failed(unsupported);
        }

        byte[] effect = MgfxWriter.Write(source, glsl, [.. constants, .. sizes], pixelBufferSize, globalsSize, textures);

        return new Gl21Stage(effect, constants, textures, bufferSize > 0, globalsSize > 0, null);
    }

    // Rewrites the stage for the effect container and returns why OpenGL 2.1 would not compile it, or
    // null. The combined sampler SPIRV-Cross names after a texture and the engine's sampler takes the
    // slot's name. A read through the size probe becomes a size parameter appended to the pixel buffer.
    // The flattened parameter block takes the pixel buffer's name, and the scene globals block the
    // globals buffer's.
    private static string? AdaptForGl21(ref string glsl, List<string> textures, ref int bufferSize, out List<ShaderBinding> sizes)
    {
        sizes = [];
        for (int slot = 0; slot <= textures.Count; slot++)
        {
            string texture = slot == 0 ? ShaderTemplate.SpriteTexture : textures[slot - 1];
            glsl = Replace(glsl, CombinedPrefix + texture + ShaderTemplate.Sampler, MgfxWriter.SamplerName(slot));

            string probe = Regex.Escape(CombinedPrefix + texture + ShaderTemplate.SizeProbe);
            if (!Regex.IsMatch(glsl, $@"\b{probe}\b"))
            {
                continue;
            }

            glsl = Regex.Replace(glsl, $@"uniform sampler2D {probe};\n", string.Empty);
            glsl = Regex.Replace(glsl, $@"texture2D\({probe}, vec2\(0\.0\)\)", $"{GlobalBlock}[{bufferSize / 16}]");

            // A read the pattern missed would name a sampler no longer declared.
            if (Regex.IsMatch(glsl, $@"\b{probe}\b"))
            {
                return $"reads the size of texture '{texture}' in a form the compiler cannot bind. Read it with TextureSize({texture}).";
            }

            sizes.Add(new ShaderBinding(ShaderTemplate.TextureSize + slot.ToString(CultureInfo.InvariantCulture), ShaderParameterKind.Vector2, false, bufferSize));
            bufferSize += 16;
        }

        if (sizes.Count > 0)
        {
            string declaration = $"uniform vec4 {GlobalBlock}[{bufferSize / 16}];";
            glsl = Regex.IsMatch(glsl, $@"uniform vec4 {GlobalBlock}\[\d+\];")
                ? Regex.Replace(glsl, $@"uniform vec4 {GlobalBlock}\[\d+\];", declaration)
                : glsl.Replace($"#version {GlslVersion}\n", $"#version {GlslVersion}\n{declaration}\n", StringComparison.Ordinal);
        }

        glsl = Replace(glsl, GlobalBlock, MgfxWriter.PixelBuffer);
        glsl = Replace(glsl, SceneGlobalsBlock, MgfxWriter.GlobalsBuffer);

        if (BuiltInCall(glsl, "texelFetch"))
        {
            return "reads a texel with Load or an index, which OpenGL 2.1 lacks. Read it with Sample(texture, (texel + 0.5) / TextureSize(texture)).";
        }

        if (BuiltInCall(glsl, "textureSize"))
        {
            return "reads a texture's size with GetDimensions, which OpenGL 2.1 lacks. Read it with TextureSize(texture).";
        }

        if (Regex.IsMatch(glsl, $@"\b{CombinedPrefix}"))
        {
            return "reads a texture other than through Sample or TextureSize, which OpenGL 2.1 lacks. Read it with Sample(texture, uv).";
        }

        if (ExtensionLine().Match(glsl) is { Success: true } extension)
        {
            return $"needs the GLSL extension {extension.Groups["name"].Value}, which OpenGL 2.1 lacks. An unsigned integer or a rounding function is the usual cause. Compute in float, as floor(x + 0.5) for round(x).";
        }

        return null;
    }

    // Whether the stage calls the GLSL built-in, as a whole identifier and not part of a game's name.
    // GLSL 1.20 spells it with a 2D suffix, from the GL_EXT_gpu_shader4 extension.
    private static bool BuiltInCall(string glsl, string function) =>
        Regex.IsMatch(glsl, $@"\b{function}(?:2D)?\s*\(");

    private static string Replace(string glsl, string name, string replacement) =>
        Regex.Replace(glsl, $@"\b{Regex.Escape(name)}\b", replacement);

    [GeneratedRegex(@"^#extension (?<name>\S+)", RegexOptions.Multiline)]
    private static partial Regex ExtensionLine();

    // The OpenGL 2.1 effect, null when its compile failed, and the parameter table its reflection read:
    // the constants at their offsets in their buffers and the textures beyond the sprite's in slot order.
    private sealed record Gl21Stage(byte[]? Effect, List<ShaderBinding> Constants, List<string> Textures, bool HasMaterialBuffer, bool HasGlobalsBuffer, string? Failure)
    {
        internal static Gl21Stage Failed(string? failure) => new(null, [], [], false, false, failure);
    }
}
