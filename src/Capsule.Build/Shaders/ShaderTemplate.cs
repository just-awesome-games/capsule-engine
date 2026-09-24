namespace Capsule.Build.Shaders;

/// <summary>
/// The engine's sprite pixel stage around a game's <c>Fragment</c>. The prelude declares what a
/// fragment reads, and the epilogue declares the pixel stage that calls the fragment and then applies
/// the flash. The vertex stage is fixed GLSL the compiler adds. The engine's own shader is this
/// template around <see cref="DefaultFragment"/>.
/// </summary>
internal static class ShaderTemplate
{
    /// <summary>The sprite texture parameter, which the batcher binds per run and no material sets.</summary>
    internal const string SpriteTexture = "SpriteTexture";

    /// <summary>The transform parameter of the vertex stage, which the batcher sets on every effect it applies.</summary>
    internal const string MatrixTransform = "MatrixTransform";

    /// <summary>The one sampler every texture is read through. The host sets its state per slot.</summary>
    internal const string Sampler = "CapsuleSampler";

    /// <summary>The pixel stage's entry point.</summary>
    internal const string EntryPoint = "CapsulePixel";

    /// <summary>What a compiler error inside the template is reported against.</summary>
    internal const string TemplateFile = "capsule-sprite-template.fx";

    /// <summary>The fragment of the engine's own sprite shader.</summary>
    internal const string DefaultFragment = """
        float4 Fragment(SpritePixel pixel)
        {
            return pixel.Texel * pixel.Tint;
        }

        """;

    /// <summary>What the default fragment is reported against. It names no file on disk.</summary>
    internal const string DefaultFragmentFile = "capsule-default-fragment.fx";

    private const string Prelude = """
        Texture2D SpriteTexture;
        SamplerState CapsuleSampler;

        float4 Sample(Texture2D source, float2 uv)
        {
            return source.Sample(CapsuleSampler, uv);
        }

        float4 SampleSprite(float2 uv)
        {
            return Sample(SpriteTexture, uv);
        }

        struct SpritePixel
        {
            float4 Texel;
            float4 Tint;
            float2 UV;
        };

        """;

    // The flash mixes the fragment's colour towards the flash colour scaled by the texel's coverage.
    // The vertex carries that colour already premultiplied by the tint's alpha, so a full flash draws a
    // premultiplied silhouette under alpha blending and adds it under additive blending, where the
    // fragment's alpha stays zero. A zero amount leaves the colour exactly as the fragment returned it.
    // The inputs are in location order: tint, flash, texture coordinate.
    private const string Epilogue = """

        float4 CapsulePixel(float4 tint : COLOR0, float4 flash : COLOR1, float2 uv : TEXCOORD0) : SV_Target0
        {
            SpritePixel pixel;
            pixel.Texel = SampleSprite(uv);
            pixel.Tint = tint;
            pixel.UV = uv;

            float4 color = Fragment(pixel);
            color.rgb = lerp(color.rgb, flash.rgb * pixel.Texel.a, flash.a);
            return color;
        }

        """;

    /// <summary>
    /// The complete pixel-stage source for <paramref name="fragment"/>. Line directives report a
    /// compiler error inside the fragment against <paramref name="fragmentPath"/> at the line the
    /// author wrote, and one inside the template against <see cref="TemplateFile"/>.
    /// </summary>
    internal static string Compose(string fragment, string fragmentPath)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(fragmentPath);

        // The compiler reads a directive's file name as a string literal.
        string quoted = fragmentPath.Replace('\\', '/').Replace("\"", "\\\"", StringComparison.Ordinal);

        // One line ending whatever the checkout wrote, so a source composes the same on every machine.
        string composed = $"#line 1 \"{TemplateFile}\"\n{Prelude}#line 1 \"{quoted}\"\n{fragment}\n#line 1 \"{TemplateFile}\"\n{Epilogue}";

        return composed.ReplaceLineEndings("\n");
    }

    /// <summary>The engine's own sprite shader, composed.</summary>
    internal static string ComposeDefault() => Compose(DefaultFragment, DefaultFragmentFile);
}
