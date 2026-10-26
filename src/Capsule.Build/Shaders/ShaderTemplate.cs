namespace Capsule.Build.Shaders;

/// <summary>
/// The engine's sprite pixel stage around a game's <c>Fragment</c>.
/// </summary>
/// <remarks>
/// The prelude declares what a fragment reads, and the epilogue declares the pixel stage that calls
/// the fragment and then applies the flash. One prelude reads every texture through one sampler for
/// OpenGL 2.1, and the bound prelude pairs each texture with a sampler of its own, as SDL's GPU API
/// binds them. The engine's own shader is this template around <see cref="DefaultFragment"/>, drawn by
/// <see cref="VertexStage"/>.
/// </remarks>
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
    /// The prefix of every name the template and the host introduce.
    /// </summary>
    /// <remarks>
    /// A game parameter or texture carrying it could be bound as the engine's. The build rejects
    /// one.
    /// </remarks>
    internal const string ReservedPrefix = "Capsule";

    /// <summary>The one sampler every texture is read through. The host sets its state per slot.</summary>
    internal const string Sampler = "CapsuleSampler";

    /// <summary>
    /// The sampler <c>TextureSize</c> reads through.
    /// </summary>
    /// <remarks>
    /// The compiler replaces each read through it with the texture's size parameter. No read through
    /// it reaches the host.
    /// </remarks>
    internal const string SizeProbe = "CapsuleSizeProbe";

    /// <summary>
    /// The prefix of a texture's size parameter, completed by its slot, the sprite's being 0. The host
    /// sets it wherever it binds the texture, and no material sees or sets it.
    /// </summary>
    internal const string TextureSize = "CapsuleTextureSize";

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
    internal const string VertexFile = "capsule-vertex.fx";

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

    /// <summary>
    /// The engine's vertex stage, the one every sprite shader shares in SDL's binding layout: the
    /// transform's four rows at offset 0 of uniform buffer 0, then the coverage switch at offset 64.
    /// </summary>
    /// <remarks>
    /// Its outputs are the pixel stage's inputs in location order: tint, flash, texture coordinate,
    /// coverage.
    /// </remarks>
    internal const string VertexStage = """
        cbuffer CapsuleVertexUniforms : register(b0, space1)
        {
            float4x4 MatrixTransform;
            float CapsuleCoverage;
        };

        struct CapsuleVertexOutput
        {
            [[vk::location(0)]] float4 Tint : COLOR0;
            [[vk::location(1)]] float4 Flash : COLOR1;
            [[vk::location(2)]] float2 UV : TEXCOORD0;
            [[vk::location(3)]] float Coverage : TEXCOORD1;
            float4 Position : SV_Position;
        };

        CapsuleVertexOutput CapsuleVertex(
            [[vk::location(0)]] float2 position : TEXCOORD0,
            [[vk::location(1)]] float4 tint : TEXCOORD1,
            [[vk::location(2)]] float4 flash : TEXCOORD2,
            [[vk::location(3)]] float2 uv : TEXCOORD3)
        {
            CapsuleVertexOutput output;
            output.Position = mul(float4(position, 0.0, 1.0), MatrixTransform);
            output.Tint = tint;
            output.Flash = flash;
            output.UV = uv;
            output.Coverage = CapsuleCoverage;
            return output;
        }

        """;

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

        """;

    // Sample reads through the sampler named for the texture, and only a texture parameter has one.
    // TextureSize asks the texture, which every format SDL's GPU API reads can.
    private const string BoundSampling = """
        #define Sample(source, uv) ((source).Sample(CapsuleSampler_##source, (uv)))

        float2 TextureSize(Texture2D source)
        {
            uint width;
            uint height;
            source.GetDimensions(width, height);
            return float2(width, height);
        }

        """;

    // What both preludes declare after their sampling.
    private const string Pixel = """
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
    // The vertex carries that colour already premultiplied by the tint's alpha. A full flash draws a
    // premultiplied silhouette under alpha blending and adds it under additive blending, where the
    // fragment's alpha stays zero. A zero amount leaves the colour exactly as the fragment returned it.
    // The inputs are in location order: tint, flash, texture coordinate, coverage. Coverage comes from
    // the vertex stage. A pixel stage reads no uniform of the engine's.
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
    internal static string Compose(string fragment, string fragmentPath) =>
        Compose(Prelude + Pixel, fragment, fragmentPath);

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

        return Compose($"Texture2D {SpriteTexture};\n{samplers}static float CapsuleSpriteCoverage;\n\n{BoundSampling}{Pixel}", fragment, fragmentPath);
    }

    private static string Compose(string prelude, string fragment, string fragmentPath)
    {
        // The compiler reads a directive's file name as a string literal.
        string quoted = fragmentPath.Replace('\\', '/').Replace("\"", "\\\"", StringComparison.Ordinal);

        // One line ending whatever the checkout wrote. A source composes the same on every machine.
        string composed = $"#line 1 \"{TemplateFile}\"\n{prelude}#line 1 \"{quoted}\"\n{fragment}\n#line 1 \"{TemplateFile}\"\n{Epilogue}";

        return composed.ReplaceLineEndings("\n");
    }
}
