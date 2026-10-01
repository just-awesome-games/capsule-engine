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

    /// <summary>
    /// The vertex-stage coverage switch, 1 while the sprite texture is single-channel and 0 otherwise.
    /// The batcher sets it when a run's texture format changes, and no material sees or sets it.
    /// </summary>
    internal const string Coverage = "CapsuleCoverage";

    /// <summary>
    /// The prefix of every name the template and the host introduce. A game parameter or texture
    /// carrying it could be bound as the engine's, so the build rejects one.
    /// </summary>
    internal const string ReservedPrefix = "Capsule";

    /// <summary>The one sampler every texture is read through. The host sets its state per slot.</summary>
    internal const string Sampler = "CapsuleSampler";

    /// <summary>
    /// The sampler <c>TextureSize</c> reads through. The compiler replaces each read through it with
    /// the texture's size parameter. No read through it reaches the host.
    /// </summary>
    internal const string SizeProbe = "CapsuleSizeProbe";

    /// <summary>
    /// The prefix of a texture's size parameter, completed by its slot, the sprite's being 0. The host
    /// sets it wherever it binds the texture, and no material sees or sets it.
    /// </summary>
    internal const string TextureSize = "CapsuleTextureSize";

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

    // A single-channel texture samples as (v, v, v, 1). The sprite's texel takes the premultiplied
    // coverage form (v, v, v, v), which draws white at opacity v under the tint. Only alpha differs.
    // TextureSize reads through the size probe at a fixed point. The compiler swaps that read for the
    // texture's size parameter. OpenGL 2.1 has no size query, and HLSL cannot name a parameter after
    // the texture a function was handed.
    private const string Prelude = """
        Texture2D SpriteTexture;
        SamplerState CapsuleSampler;
        SamplerState CapsuleSizeProbe;
        static float CapsuleSpriteCoverage;

        float4 Sample(Texture2D source, float2 uv)
        {
            return source.Sample(CapsuleSampler, uv);
        }

        float2 TextureSize(Texture2D source)
        {
            return source.Sample(CapsuleSizeProbe, float2(0.0, 0.0)).xy;
        }

        float4 SampleSprite(float2 uv)
        {
            float4 texel = Sample(SpriteTexture, uv);
            texel.a += (texel.r - texel.a) * CapsuleSpriteCoverage;
            return texel;
        }

        struct SpritePixel
        {
            float4 Texel;
            float4 Tint;
            float2 UV;
            float2 TextureSize;
        };

        """;

    // The flash mixes the fragment's colour towards the flash colour scaled by the texel's coverage.
    // The vertex carries that colour already premultiplied by the tint's alpha, so a full flash draws a
    // premultiplied silhouette under alpha blending and adds it under additive blending, where the
    // fragment's alpha stays zero. A zero amount leaves the colour exactly as the fragment returned it.
    // The inputs are in location order: tint, flash, texture coordinate, coverage. Coverage comes from
    // the vertex stage, so a pixel stage reads no uniform of the engine's.
    private const string Epilogue = """

        float4 CapsulePixel(float4 tint : COLOR0, float4 flash : COLOR1, float2 uv : TEXCOORD0, float coverage : TEXCOORD1) : SV_Target0
        {
            CapsuleSpriteCoverage = coverage;
            SpritePixel pixel;
            pixel.Texel = SampleSprite(uv);
            pixel.Tint = tint;
            pixel.UV = uv;
            pixel.TextureSize = TextureSize(SpriteTexture);

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
        // The compiler reads a directive's file name as a string literal.
        string quoted = fragmentPath.Replace('\\', '/').Replace("\"", "\\\"", StringComparison.Ordinal);

        // One line ending whatever the checkout wrote, so a source composes the same on every machine.
        string composed = $"#line 1 \"{TemplateFile}\"\n{Prelude}#line 1 \"{quoted}\"\n{fragment}\n#line 1 \"{TemplateFile}\"\n{Epilogue}";

        return composed.ReplaceLineEndings("\n");
    }

    /// <summary>The engine's own sprite shader, composed.</summary>
    internal static string ComposeDefault() => Compose(DefaultFragment, DefaultFragmentFile);
}
