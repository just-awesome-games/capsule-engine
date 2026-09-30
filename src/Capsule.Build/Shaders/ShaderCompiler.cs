using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>
/// The one seam a pixel-stage source becomes a compiled effect through. DXC compiles the HLSL to
/// SPIR-V, SPIRV-Cross reflects its parameters and writes it as GLSL, and <see cref="MgfxWriter"/>
/// packs it with the engine's vertex stage. Both tools are native binaries for the host's operating
/// system and architecture, so a workstation installs nothing.
/// </summary>
internal static partial class ShaderCompiler
{
    // The GLSL the desktop host's OpenGL 2.1 context accepts.
    private const string GlslVersion = "120";

    private const string CombinedPrefix = "SPIRV_Cross_Combined";

    // The parameter block SPIRV-Cross flattens the global constants into.
    private const string GlobalBlock = "type_Globals";

    /// <summary>Compiles the composed source at <paramref name="sourcePath"/>.</summary>
    /// <param name="tools">The downloaded tools.</param>
    /// <param name="sourcePath">The composed pixel-stage source. Intermediate files are written beside it.</param>
    internal static ShaderCompilation Compile(ShaderTools tools, string sourcePath)
    {
        string source = Path.GetFullPath(sourcePath);
        string spirv = Path.ChangeExtension(source, ".spv");

        // Run beside the source and handed its bare name, so no machine path reaches a diagnostic's
        // anchor or a shipped file.
        (int exit, string output, string errors) = Run(
            Binary(tools.Dxc, "dxc", inBin: true),
            Path.GetDirectoryName(source)!,
            "-spirv",
            "-T",
            "ps_6_0",
            "-E",
            ShaderTemplate.EntryPoint,
            "-Fo",
            Path.GetFileName(spirv),
            Path.GetFileName(source));

        List<ShaderDiagnostic> diagnostics = Diagnostics(output + '\n' + errors);
        if (exit != 0 || !File.Exists(spirv))
        {
            return new ShaderCompilation(
                null,
                [],
                diagnostics,
                diagnostics.Any(static diagnostic => !diagnostic.Warning) ? null : "the shader compiler failed: " + Flatten(output + errors));
        }

        try
        {
            string crossCompiler = Binary(tools.SpirvCross, "spirv-cross", inBin: false);
            (exit, output, errors) = Run(crossCompiler, Path.GetDirectoryName(source)!, spirv, "--reflect");
            if (exit != 0)
            {
                return Failed("the shader cross-compiler could not reflect it: " + Flatten(output + errors), diagnostics);
            }

            if (Reflect(output, out List<PixelConstant> constants, out int bufferSize, out List<string> textures) is { } refused)
            {
                return Failed(refused, diagnostics);
            }

            List<string> arguments =
            [
                spirv, "--version", GlslVersion, "--no-es", "--no-420pack-extension", "--flatten-ubo",
            ];
            for (int location = 0; location < MgfxWriter.Varyings.Length; location++)
            {
                arguments.AddRange(["--rename-interface-variable", "in", location.ToString(CultureInfo.InvariantCulture), MgfxWriter.Varyings[location]]);
            }

            (exit, output, errors) = Run(crossCompiler, Path.GetDirectoryName(source)!, [.. arguments]);
            if (exit != 0)
            {
                return Failed("the shader cross-compiler could not write it as GLSL: " + Flatten(output + errors), diagnostics);
            }

            string glsl = output.ReplaceLineEndings("\n");
            if (Bind(ref glsl, textures, ref bufferSize, out List<PixelConstant> sizes) is { } unsupported)
            {
                return Failed(unsupported, diagnostics);
            }

            byte[] effect = MgfxWriter.Write(Path.GetFileName(source), glsl, [.. constants, .. sizes], bufferSize, textures);

            List<ShaderParameter> parameters = [.. constants.Select(static constant => new ShaderParameter(constant.Name, constant.Kind))];
            parameters.AddRange(textures.Select(static texture => new ShaderParameter(texture, ShaderParameterKind.Texture)));

            return new ShaderCompilation(effect, parameters, diagnostics, null);
        }
        finally
        {
            File.Delete(spirv);
        }
    }

    // Binds each texture to its slot. The combined sampler SPIRV-Cross names after a texture and the
    // engine's sampler takes the slot's name. A read through the size probe becomes a size parameter
    // appended to the pixel buffer. The flattened parameter block takes the pixel buffer's name.
    // Returns why the stage would not compile on OpenGL 2.1, or null.
    private static string? Bind(ref string glsl, List<string> textures, ref int bufferSize, out List<PixelConstant> sizes)
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

            sizes.Add(new PixelConstant(ShaderTemplate.TextureSize + slot.ToString(CultureInfo.InvariantCulture), ShaderParameterKind.Vector2, bufferSize));
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

    // The parameter table from SPIRV-Cross's reflection: the global block's members with their
    // offsets, and every texture beyond the sprite's. Returns why a parameter cannot be set, or null.
    private static string? Reflect(string json, out List<PixelConstant> constants, out int bufferSize, out List<string> textures)
    {
        constants = [];
        textures = [];
        bufferSize = 0;

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("ubos", out JsonElement blocks))
        {
            foreach (JsonElement block in blocks.EnumerateArray())
            {
                if (block.GetProperty("name").GetString() != "type.$Globals")
                {
                    return $"declares the constant buffer '{block.GetProperty("name").GetString()}'. Declare a shader parameter as a global instead.";
                }

                bufferSize = (block.GetProperty("block_size").GetInt32() + 15) / 16 * 16;
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
                        return $"declares parameter '{name}', which a Material cannot set. A shader parameter is a float, float2, float3, float4 or Texture2D, and no array.";
                    }

                    constants.Add(new PixelConstant(name, settable, member.GetProperty("offset").GetInt32()));
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
                if (sampler.GetProperty("name").GetString() is not (ShaderTemplate.Sampler or ShaderTemplate.SizeProbe))
                {
                    return "declares a sampler of its own. Read a texture with Sample(texture, uv), which samples as the sprite does.";
                }
            }
        }

        return null;
    }

    // Whether a game parameter's name is the engine's: the vertex stage's transform, or any name the
    // template or the host binds, all of which carry the reserved prefix.
    private static bool Reserved(string name) =>
        name == ShaderTemplate.MatrixTransform || name.StartsWith(ShaderTemplate.ReservedPrefix, StringComparison.Ordinal);

    // DXC's 'file:line:column: error: message', one per diagnostic. The source excerpt and caret
    // lines that follow each one match nothing.
    private static List<ShaderDiagnostic> Diagnostics(string output)
    {
        List<ShaderDiagnostic> diagnostics = [];
        foreach (string line in output.Split('\n'))
        {
            Match match = DiagnosticLine().Match(line.TrimEnd('\r'));
            if (match.Success)
            {
                diagnostics.Add(new ShaderDiagnostic(
                    match.Groups["file"].Value,
                    int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["column"].Value, CultureInfo.InvariantCulture),
                    match.Groups["severity"].Value == "warning",
                    match.Groups["message"].Value.Trim()));
            }
        }

        return diagnostics;
    }

    // The tool's binary for this host. Each package lays a folder out per operating system and
    // architecture, macOS as one universal binary. NuGet extracts without the executable bit, so it is
    // restored before the first run.
    private static string Binary(string package, string name, bool inBin)
    {
        string host;
        if (OperatingSystem.IsWindows())
        {
            host = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "windows-arm64" : "windows-x64";
        }
        else if (OperatingSystem.IsMacOS())
        {
            host = "osx";
        }
        else
        {
            host = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        }

        string path = OperatingSystem.IsWindows()
            ? Path.Combine(package, "binaries", host, name + ".exe")
            : Path.Combine(package, "binaries", host, inBin ? "bin" : string.Empty, name);

        if (!File.Exists(path))
        {
            throw new IOException($"The shader tool '{path}' is missing. Restore the project, which downloads it for a game with shaders.");
        }

        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode Execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            UnixFileMode mode = File.GetUnixFileMode(path);
            if ((mode & UnixFileMode.UserExecute) == 0)
            {
                File.SetUnixFileMode(path, mode | Execute);
            }
        }

        return path;
    }

    private static (int ExitCode, string Output, string Errors) Run(string executable, string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo start = new(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)
            ?? throw new IOException($"The shader tool '{executable}' did not start.");

        Task<string> errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        string error = errors.GetAwaiter().GetResult();
        process.WaitForExit();

        return (process.ExitCode, output, error);
    }

    private static ShaderCompilation Failed(string failure, IReadOnlyList<ShaderDiagnostic>? diagnostics = null) =>
        new(null, [], diagnostics ?? [], failure);

    private static string Flatten(string output) => output.Trim().ReplaceLineEndings(" ");

    [GeneratedRegex(@"^#extension (?<name>\S+)", RegexOptions.Multiline)]
    private static partial Regex ExtensionLine();

    [GeneratedRegex(@"^(?<file>.+?):(?<line>\d+):(?<column>\d+):\s*(?<severity>error|warning):\s*(?<message>.*)$")]
    private static partial Regex DiagnosticLine();
}
