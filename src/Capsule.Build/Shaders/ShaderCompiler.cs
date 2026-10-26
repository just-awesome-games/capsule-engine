using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>The one seam a shader stage's source becomes a container per GPU format through.</summary>
/// <remarks>
/// DXC compiles the HLSL to SPIR-V in SDL's binding layout. SPIRV-Cross writes that SPIR-V as MSL, and
/// as HLSL that DXC compiles to signed DXIL, each with the bindings the SPIR-V decorates, as SDL's own
/// shader tool does. A further format's tool compiles that HLSL. Both tools are native binaries for the
/// host's operating system and architecture, and a workstation installs nothing.
/// </remarks>
internal static partial class ShaderCompiler
{
    /// <summary>SPIR-V, which SDL's GPU API reads on Vulkan.</summary>
    internal const string Spirv = "spirv";

    /// <summary>DXIL, which SDL's GPU API reads on Direct3D 12.</summary>
    internal const string Dxil = "dxil";

    /// <summary>MSL 2.1 source, which SDL's GPU API reads on Metal.</summary>
    internal const string Msl = "msl";

    /// <summary>The effect the desktop host draws with until it leaves MonoGame, its code an OpenGL 2.1 effect.</summary>
    internal const string Glsl = "glsl";

    /// <summary>Every format the engine compiles a fragment to.</summary>
    internal static readonly string[] Formats = [Spirv, Dxil, Msl, Glsl];

    // SDL's descriptor sets: a stage's textures, samplers and storage in one, its uniform buffers in
    // the next. The material's uniform buffer is binding 0 and the scene globals' binding 1.
    private const int FragmentResources = 2;
    private const int FragmentUniforms = 3;
    private const int VertexResources = 0;
    private const int VertexUniforms = 1;

    // The block DXC gathers a source's loose parameters into, as SPIR-V names its variable.
    private const string LooseParameters = "$Globals";

    private const string VertexUniformBlock = "CapsuleVertexUniforms";

    private const string VertexSprites = "CapsuleSprites";

    // The entry point SPIRV-Cross gives the HLSL it writes.
    private const string HlslEntryPoint = "main";

    /// <summary>Compiles a game's fragment, wrapped in the engine's template, to every format.</summary>
    /// <param name="tools">The downloaded tools, and the further formats.</param>
    /// <param name="basePath">Where the intermediate files are written and deleted, as a path without extension in a directory of its own.</param>
    /// <param name="fragment">The game's fragment.</param>
    /// <param name="fragmentPath">What an error inside the fragment is reported against.</param>
    internal static ShaderCompilation CompileFragment(ShaderTools tools, string basePath, string fragment, string fragmentPath)
    {
        basePath = Path.GetFullPath(basePath);
        List<ShaderDiagnostic> diagnostics = [];
        try
        {
            // The OpenGL 2.1 compile reports the game's errors and reads the parameter table the bound
            // compile pairs samplers from.
            Write(basePath + ".fx", ShaderTemplate.Compose(fragment, fragmentPath));
            Gl21Stage gl21 = CompileGl21(tools, basePath, diagnostics);
            if (gl21.Effect is not { } effect)
            {
                return Failed(gl21.Failure, diagnostics);
            }

            Dictionary<string, (int, int)> places = new()
            {
                [LooseParameters] = (FragmentUniforms, 0),
                [ShaderTemplate.SceneGlobalsBlock] = (FragmentUniforms, 1),
            };

            List<ShaderBinding> table = [.. gl21.Constants];
            for (int slot = 0; slot <= gl21.Textures.Count; slot++)
            {
                string texture = slot == 0 ? ShaderTemplate.SpriteTexture : gl21.Textures[slot - 1];
                places[texture] = (FragmentResources, slot);
                places[ShaderTemplate.PairedSampler + texture] = (FragmentResources, slot);
                if (slot > 0)
                {
                    table.Add(new ShaderBinding(texture, ShaderParameterKind.Texture, false, slot));
                }
            }

            ShaderContainer layout = new(
                string.Empty,
                ShaderStage.Fragment,
                ShaderTemplate.EntryPoint,
                Samplers: gl21.Textures.Count + 1,
                StorageTextures: 0,
                StorageBuffers: 0,
                UniformBuffers: gl21.HasGlobalsBuffer ? 2 : gl21.HasMaterialBuffer ? 1 : 0,
                [.. table],
                []);

            Write(basePath + ".bound.fx", ShaderTemplate.ComposeBound(fragment, fragmentPath, gl21.Textures));
            List<ShaderContainer> shaders = [layout with { Format = Glsl, Code = effect }];
            if (!CompileBound(tools, basePath, layout, places, shaders, diagnostics, out string? failure))
            {
                return Failed(failure, diagnostics);
            }

            ShaderParameter[] parameters = [.. table.Select(static parameter => new ShaderParameter(parameter.Name, parameter.Kind, parameter.Global))];

            return new ShaderCompilation([.. shaders], parameters, diagnostics, null);
        }
        finally
        {
            DeleteIntermediates(basePath);
        }
    }

    /// <summary>Compiles the engine's vertex stage to every format but <see cref="Glsl"/>, whose effect carries its own.</summary>
    /// <param name="tools">The downloaded tools, and the further formats.</param>
    /// <param name="basePath">Where the intermediate files are written and deleted, as a path without extension in a directory of its own.</param>
    internal static ShaderCompilation CompileVertex(ShaderTools tools, string basePath)
    {
        basePath = Path.GetFullPath(basePath);
        List<ShaderDiagnostic> diagnostics = [];
        try
        {
            Write(basePath + ".bound.fx", $"#line 1 \"{ShaderTemplate.VertexFile}\"\n{ShaderTemplate.VertexStage}");
            ShaderContainer layout = new(string.Empty, ShaderStage.Vertex, ShaderTemplate.VertexEntryPoint, 0, 0, StorageBuffers: 1, UniformBuffers: 1, [], []);
            List<ShaderContainer> shaders = [];
            Dictionary<string, (int, int)> places = new()
            {
                [VertexSprites] = (VertexResources, 0),
                [VertexUniformBlock] = (VertexUniforms, 0),
            };
            return CompileBound(tools, basePath, layout, places, shaders, diagnostics, out string? failure)
                ? new ShaderCompilation([.. shaders], [], diagnostics, null)
                : Failed(failure, diagnostics);
        }
        finally
        {
            DeleteIntermediates(basePath);
        }
    }

    /// <summary>Whether DXIL carries the validator's hash, without which Direct3D 12 refuses it.</summary>
    internal static bool Signed(ReadOnlySpan<byte> dxil) => dxil.Length >= 20 && dxil[4..20].ContainsAnyExcept((byte)0);

    // Compiles basePath + ".bound.fx" to SPIR-V, places its resources, derives every other format from
    // it, and adds a container per format to shaders, each a copy of layout. Returns whether it
    // compiled, and else why it failed where no diagnostic anchors it.
    private static bool CompileBound(ShaderTools tools, string basePath, ShaderContainer layout, IReadOnlyDictionary<string, (int, int)> places, List<ShaderContainer> shaders, List<ShaderDiagnostic> diagnostics, out string? failure)
    {
        string directory = Path.GetDirectoryName(basePath)!;
        string name = Path.GetFileName(basePath);
        string stage = layout.Stage == ShaderStage.Vertex ? "vertex" : "fragment";
        string profile = layout.Stage == ShaderStage.Vertex ? "vs_6_0" : "ps_6_0";
        string spirv = name + ".bound.spv";

        (int exit, string output, string errors) = Run(Binary(tools, "dxc"), directory, "-spirv", "-T", profile, "-E", layout.EntryPoint, "-Fo", spirv, name + ".bound.fx");
        if (exit != 0)
        {
            List<ShaderDiagnostic> errorsOnly = [.. Diagnostics(output + '\n' + errors).Where(static diagnostic => !diagnostic.Warning).Select(Paired)];
            diagnostics.AddRange(errorsOnly);
            failure = errorsOnly.Count > 0 ? null : "the shader compiler failed: " + Flatten(output + errors);
            return false;
        }

        byte[] module = File.ReadAllBytes(Path.Combine(directory, spirv));
        if (SpirvBindings.Place(module, places) is { } unplaced)
        {
            failure = $"binds '{unplaced}', a resource the engine does not place. Declare parameters and textures at file scope, and scene globals in the {ShaderTemplate.SceneGlobalsBlock} block.";
            return false;
        }

        File.WriteAllBytes(Path.Combine(directory, spirv), module);
        shaders.Add(layout with { Format = Spirv, Code = module });

        string crossCompiler = Binary(tools, "spirv-cross");
        Task<(int, string, string)> msl = Task.Run(() => Run(crossCompiler, directory, spirv, "--msl", "--msl-version", "20100", "--msl-decoration-binding"));
        string? dxilFailure = CompileDxil(tools, directory, name, profile, crossCompiler, layout, shaders);
        (exit, output, errors) = msl.GetAwaiter().GetResult();
        failure = dxilFailure;
        if (failure is not null)
        {
            return false;
        }

        if (exit != 0)
        {
            failure = "the shader cross-compiler could not write it as MSL: " + Flatten(output + errors);
            return false;
        }

        shaders.Add(layout with { Format = Msl, Code = Encoding.UTF8.GetBytes(output.ReplaceLineEndings("\n")) });

        foreach (ShaderFormatTool further in tools.Further)
        {
            string compiled = name + "." + further.Format;
            (exit, output, errors) = Run(further.Tool, directory, "--stage", stage, "--entry", HlslEntryPoint, "--hlsl", name + ".bound.hlsl", "--spirv", spirv, "--out", compiled);
            if (exit != 0 || !File.Exists(Path.Combine(directory, compiled)))
            {
                failure = $"the {further.Format} shader tool failed: " + Flatten(output + errors);
                return false;
            }

            shaders.Add(layout with { Format = further.Format, EntryPoint = HlslEntryPoint, Code = File.ReadAllBytes(Path.Combine(directory, compiled)) });
        }

        return true;
    }

    // Writes the placed SPIR-V as HLSL and compiles that to DXIL, adding its container to shaders.
    // Returns why it failed, or null.
    private static string? CompileDxil(ShaderTools tools, string directory, string name, string profile, string crossCompiler, ShaderContainer layout, List<ShaderContainer> shaders)
    {
        (int exit, string output, string errors) = Run(crossCompiler, directory, name + ".bound.spv", "--hlsl", "--shader-model", "60", "--output", name + ".bound.hlsl");
        if (exit != 0)
        {
            return "the shader cross-compiler could not write it as HLSL: " + Flatten(output + errors);
        }

        string dxil = name + "." + Dxil;
        (exit, output, errors) = Run(Binary(tools, "dxc"), directory, "-T", profile, "-E", HlslEntryPoint, "-Fo", dxil, name + ".bound.hlsl");
        if (exit != 0)
        {
            return "the shader compiler could not compile it to DXIL: " + Flatten(output + errors);
        }

        byte[] signed = File.ReadAllBytes(Path.Combine(directory, dxil));
        if (!Signed(signed))
        {
            return "the shader compiler wrote DXIL without the validator's signature, which Direct3D 12 refuses. Restore the shader tools, whose DXC validates and signs on every host.";
        }

        shaders.Add(layout with { Format = Dxil, EntryPoint = HlslEntryPoint, Code = signed });

        return null;
    }

    // An undeclared paired sampler means Sample was handed something other than a texture parameter.
    private static ShaderDiagnostic Paired(ShaderDiagnostic diagnostic) =>
        diagnostic.Message.Contains($"'{ShaderTemplate.PairedSampler}", StringComparison.Ordinal)
            ? diagnostic with { Message = "samples a texture that is not a texture parameter. Call Sample(texture, uv) with the parameter itself, not a function argument holding it." }
            : diagnostic;

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

    // The tool's binary for this host. The package lays a folder out per runtime identifier, macOS as
    // one universal binary. NuGet extracts without the executable bit. It is restored before the
    // first run.
    internal static string Binary(ShaderTools tools, string name)
    {
        string architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        string host = OperatingSystem.IsWindows() ? "win-" + architecture
            : OperatingSystem.IsMacOS() ? "osx"
            : "linux-" + architecture;
        string path = Path.Combine(tools.Package, "tools", host, "bin", OperatingSystem.IsWindows() ? name + ".exe" : name);

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

    // Run beside the sources and handed their bare names. No machine path reaches a diagnostic's
    // anchor or a shipped file.
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
            ?? throw new IOException($"The shader tool '{executable}' did not start. Restore the project, and check that the tool runs on this host.");

        Task<string> errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        string error = errors.GetAwaiter().GetResult();
        process.WaitForExit();

        return (process.ExitCode, output, error);
    }

    private static void Write(string path, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    // Every file a compile wrote. Its directory is its own, and holds no other shader's files.
    private static void DeleteIntermediates(string basePath)
    {
        string directory = Path.GetDirectoryName(basePath)!;
        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                File.Delete(file);
            }
        }
    }

    private static ShaderCompilation Failed(string? failure, IReadOnlyList<ShaderDiagnostic> diagnostics) =>
        new(null, [], diagnostics, failure);

    private static string Flatten(string output) => output.Trim().ReplaceLineEndings(" ");

    [GeneratedRegex(@"^(?<file>.+?):(?<line>\d+):(?<column>\d+):\s*(?<severity>error|warning):\s*(?<message>.*)$")]
    private static partial Regex DiagnosticLine();
}
