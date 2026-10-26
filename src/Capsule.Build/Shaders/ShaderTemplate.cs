namespace Capsule.Build.Shaders;

/// <summary>
/// The engine's sprite pixel stage around a game's <c>Fragment</c>.
/// </summary>
/// <remarks>
/// The stages are HLSL files beside the runtime's compiled sprite shaders: <c>vertex.hlsl</c>, and
/// <c>pixel.hlsl</c> with a marked line the fragment takes. A sampling header goes above the pixel stage.
/// The reflected one reads every texture through one sampler, which names the textures a fragment reads,
/// and the bound one pairs each texture with a sampler of its own, as SDL's GPU API binds them. The
/// engine's own shader is the pixel stage around <see cref="DefaultFragment"/>.
/// </remarks>
internal static class ShaderTemplate
{
    /// <summary>The sprite texture parameter, which the renderer binds per draw and no material sets.</summary>
    internal const string SpriteTexture = "SpriteTexture";

    /// <summary>
    /// The prefix of every name the template and the host introduce.
    /// </summary>
    /// <remarks>
    /// A game parameter or texture carrying it could be bound as the engine's. The build rejects
    /// one.
    /// </remarks>
    internal const string ReservedPrefix = "Capsule";

    /// <summary>The one sampler the reflected prelude reads every texture through. No shipped stage declares it.</summary>
    internal const string Sampler = "CapsuleSampler";

    /// <summary>
    /// The one constant buffer a fragment may declare. Every parameter in it is a scene global, set on
    /// the scene and shared by every material of the shader.
    /// </summary>
    /// <remarks>
    /// Parameters outside it are the material's.
    /// </remarks>
    internal const string SceneGlobalsBlock = "SceneGlobals";

    /// <summary>The pixel stage's entry point.</summary>
    internal const string EntryPoint = "CapsulePixel";

    /// <summary>The vertex stage's entry point.</summary>
    internal const string VertexEntryPoint = "CapsuleVertex";

    /// <summary>
    /// The prefix of the bound prelude's sampler for a texture, completed by the texture's name. Each sits
    /// at its texture's slot.
    /// </summary>
    internal const string PairedSampler = "CapsuleSampler_";

    /// <summary>What a compiler error inside the vertex stage is reported against.</summary>
    internal const string VertexFile = "vertex.hlsl";

    /// <summary>What a compiler error inside the pixel stage is reported against.</summary>
    internal const string TemplateFile = "pixel.hlsl";

    /// <summary>What a compiler error inside the default fragment is reported against.</summary>
    internal const string DefaultFragmentFile = "sprite.fx";

    // The line in the pixel stage the fragment takes.
    private const string FragmentMarker = "// <Fragment>\n";

    /// <summary>The engine's vertex stage, the one every sprite shader shares.</summary>
    internal static readonly string VertexStage = Read(VertexFile);

    /// <summary>The fragment of the engine's own sprite shader.</summary>
    internal static readonly string DefaultFragment = Read(DefaultFragmentFile);

    // The pixel stage before and after the fragment.
    private static readonly (string Head, string Tail, int TailLine) Pixel = SplitAtFragment(Read(TemplateFile));

    // Reads every texture through one sampler, for the compile whose reflection names the textures the
    // bound prelude pairs samplers with. No stage compiled from it ships.
    private const string Prelude = """
        Texture2D SpriteTexture;
        SamplerState CapsuleSampler;
        static float CapsuleSpriteCoverage;

        float4 Sample(Texture2D source, float2 uv)
        {
            return source.Sample(CapsuleSampler, uv);
        }

        """;

    // Sample reads through the sampler named for the texture, and only a texture parameter has one.
    private const string BoundSampling = """
        #define Sample(source, uv) ((source).Sample(CapsuleSampler_##source, (uv)))

        """;

    /// <summary>
    /// The complete pixel-stage source for <paramref name="fragment"/>. Line directives report a
    /// compiler error inside the fragment against <paramref name="fragmentPath"/> at the line the
    /// author wrote, and one inside the stage against <see cref="TemplateFile"/> at its own line.
    /// </summary>
    internal static string Compose(string fragment, string fragmentPath) =>
        Compose(Prelude, fragment, fragmentPath);

    /// <summary>
    /// The pixel-stage source for <paramref name="fragment"/> in SDL's binding layout, declaring a sampler
    /// for the sprite's texture and for each of <paramref name="textures"/>. Line directives report as
    /// <see cref="Compose(string, string)"/>'s do.
    /// </summary>
    /// <param name="fragment">The game's fragment.</param>
    /// <param name="fragmentPath">What an error inside the fragment is reported against.</param>
    /// <param name="textures">The fragment's texture parameters beyond the sprite's.</param>
    internal static string ComposeBound(string fragment, string fragmentPath, IEnumerable<string> textures)
    {
        string samplers = string.Concat(textures.Prepend(SpriteTexture).Select(static texture => $"SamplerState {PairedSampler}{texture};\n"));

        return Compose($"Texture2D {SpriteTexture};\n{samplers}static float CapsuleSpriteCoverage;\n\n{BoundSampling}", fragment, fragmentPath);
    }

    // The sampling header reports as the pixel stage's first line, since no file holds it.
    private static string Compose(string prelude, string fragment, string fragmentPath)
    {
        // The compiler reads a directive's file name as a string literal.
        string quoted = fragmentPath.Replace('\\', '/').Replace("\"", "\\\"", StringComparison.Ordinal);

        // One line ending whatever the checkout wrote. A source composes the same on every machine.
        string composed = $"#line 1 \"{TemplateFile}\"\n{prelude}#line 1 \"{TemplateFile}\"\n{Pixel.Head}#line 1 \"{quoted}\"\n{fragment}\n#line {Pixel.TailLine} \"{TemplateFile}\"\n{Pixel.Tail}";

        return composed.ReplaceLineEndings("\n");
    }

    // One of the stage files the build embeds under its own name.
    private static string Read(string file)
    {
        using Stream stream = typeof(ShaderTemplate).Assembly.GetManifestResourceStream("Capsule.Build.Shaders." + file)!;
        using StreamReader reader = new(stream);

        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    // The pixel stage on either side of the fragment's line, and the line its tail starts on.
    private static (string Head, string Tail, int TailLine) SplitAtFragment(string pixel)
    {
        int at = pixel.IndexOf(FragmentMarker, StringComparison.Ordinal);
        string head = pixel[..at];

        return (head, pixel[(at + FragmentMarker.Length)..], head.Count('\n') + 2);
    }
}
