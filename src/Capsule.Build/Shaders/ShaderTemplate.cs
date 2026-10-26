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
    /// <summary>The sprite texture parameter, which the renderer binds per draw and no material sets.</summary>
    internal const string SpriteTexture = "SpriteTexture";

    /// <summary>The transform parameter of the vertex stage, which the renderer pushes with every draw.</summary>
    internal const string MatrixTransform = "MatrixTransform";

    /// <summary>
    /// The vertex-stage coverage switch, 1 while the sprite texture is single-channel and 0 otherwise.
    /// The renderer pushes it with every draw, and no material sees or sets it.
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
    /// The engine's vertex stage, the one every sprite shader shares in SDL's binding layout. It reads one
    /// sprite per six vertices from the storage buffer in slot 0, and its uniforms from uniform buffer 0.
    /// </summary>
    /// <remarks>
    /// The sprite and the uniforms are the runtime's <c>SpriteInstance</c> and <c>SpriteUniforms</c>,
    /// member for member, and the placement is <c>SpriteQuad.Expand</c>'s. Its outputs are the pixel
    /// stage's inputs in location order: tint, flash, texture coordinate, coverage.
    /// </remarks>
    internal const string VertexStage = """
        struct CapsuleSprite
        {
            float2 PreviousPosition;
            float2 Position;
            float PreviousRotation;
            float Rotation;
            float2 Origin;
            float2 Size;
            uint Region;
            uint Extent;
            uint Tint;
            uint Flash;
            uint Flags;
            uint Padding;
        };

        StructuredBuffer<CapsuleSprite> CapsuleSprites : register(t0, space0);

        cbuffer CapsuleVertexUniforms : register(b0, space1)
        {
            row_major float4x4 MatrixTransform;
            float2 CapsuleTexelSize;
            float2 CapsuleLayerCorner;
            float2 CapsuleFrameCorner;
            float CapsuleAlpha;
            float CapsuleGrid;
            float CapsuleCoverage;
            uint CapsuleFirstSprite;
        };

        struct CapsuleVertexOutput
        {
            [[vk::location(0)]] float4 Tint : COLOR0;
            [[vk::location(1)]] float4 Flash : COLOR1;
            [[vk::location(2)]] float2 UV : TEXCOORD0;
            [[vk::location(3)]] float Coverage : TEXCOORD1;
            float4 Position : SV_Position;
        };

        static const uint CapsuleFlipX = 1;
        static const uint CapsuleFlipY = 2;
        static const uint CapsuleWhole = 4;
        static const float CapsulePi = 3.14159265358979;
        static const float CapsuleTau = 6.28318530717959;

        float4 CapsuleUnpack(uint color)
        {
            return float4(color & 0xFF, (color >> 8) & 0xFF, (color >> 16) & 0xFF, color >> 24) / 255.0;
        }

        // The whole surface pixel value lands on, counted from origin. A midpoint rounds down by a margin
        // wider than float noise.
        float CapsulePixel(float origin, float value, float scale)
        {
            precise float offset = ((value - origin) * scale) + 0.5;
            return floor(offset - (1.0 / 32.0));
        }

        float2 CapsulePixel(float2 origin, float2 value, float scale)
        {
            return float2(CapsulePixel(origin.x, value.x, scale), CapsulePixel(origin.y, value.y, scale));
        }

        // One sprite's six vertices, numbered from SV_VertexID. The arithmetic and its order are the engine's
        // CPU placement of a sprite, step for step.
        CapsuleVertexOutput CapsuleVertex(uint vertexId : SV_VertexID)
        {
            CapsuleSprite sprite = CapsuleSprites[CapsuleFirstSprite + (vertexId / 6)];

            // Top left, top right, bottom left, then top right, bottom right, bottom left.
            uint corner = (0xB64u >> ((vertexId % 6) * 2)) & 3;
            float2 axes = float2(corner & 1, corner >> 1);

            float2 texels = float2(sprite.Extent & 0xFFFF, sprite.Extent >> 16);
            float2 region = float2(sprite.Region & 0xFFFF, sprite.Region >> 16);
            bool whole = (sprite.Flags & CapsuleWhole) != 0;
            precise float2 texTopLeft = whole ? float2(0.0, 0.0) : region * CapsuleTexelSize;
            precise float2 texBottomRight = whole ? float2(1.0, 1.0) : (region + texels) * CapsuleTexelSize;

            precise float2 position = sprite.Position;
            precise float2 origin = sprite.Origin;
            precise float2 scale = sprite.Size;
            precise float sine = 0.0;
            precise float cosine = 1.0;
            bool turned = false;

            if (whole)
            {
                if (sprite.Rotation != 0.0)
                {
                    sincos(sprite.Rotation, sine, cosine);
                    turned = true;
                }
            }
            else
            {
                if (any(sprite.PreviousPosition != sprite.Position))
                {
                    position = sprite.PreviousPosition + ((sprite.Position - sprite.PreviousPosition) * CapsuleAlpha);
                }

                precise float rotation = sprite.Rotation;
                if (sprite.PreviousRotation != sprite.Rotation)
                {
                    precise float turn = sprite.Rotation - sprite.PreviousRotation;
                    turn -= round(turn / CapsuleTau) * CapsuleTau;
                    if (turn <= -CapsulePi)
                    {
                        turn += CapsuleTau;
                    }

                    rotation = sprite.PreviousRotation + (turn * CapsuleAlpha);
                }

                precise float quarters = rotation * (2.0 / CapsulePi);
                precise float nearest = round(quarters);
                scale = sprite.Size / texels;

                if (CapsuleGrid > 0.0 && (rotation == 0.0 || abs(quarters - nearest) <= 1e-4))
                {
                    // Every edge on the grid, at the exact quarter turn.
                    int turns = rotation == 0.0 ? 0 : ((int(nearest) % 4) + 4) % 4;
                    precise float2 a = -(origin * scale);
                    precise float2 b = sprite.Size - (origin * scale);
                    precise float2 low = position + (turns == 0 ? a : turns == 1 ? float2(-b.y, a.x) : turns == 2 ? -b : float2(a.y, -b.x));
                    precise float2 high = rotation == 0.0 ? low + sprite.Size
                        : position + (turns == 0 ? b : turns == 1 ? float2(-a.y, b.x) : turns == 2 ? -a : float2(b.y, -a.x));
                    float2 left = CapsulePixel(CapsuleLayerCorner, low, CapsuleGrid);
                    float2 extent = CapsulePixel(CapsuleLayerCorner, high, CapsuleGrid) - left;
                    precise float2 snappedLow = CapsuleFrameCorner + (left / CapsuleGrid);
                    precise float2 snappedHigh = snappedLow + (extent / CapsuleGrid);
                    float2 pixels = (turns & 1) == 0 ? extent : extent.yx;
                    position = turns == 0 ? snappedLow
                        : turns == 1 ? float2(snappedHigh.x, snappedLow.y)
                        : turns == 2 ? snappedHigh
                        : float2(snappedLow.x, snappedHigh.y);
                    scale = pixels / (texels * CapsuleGrid);
                    origin = float2(0.0, 0.0);
                    turned = turns != 0;
                    sine = turns == 1 ? 1.0 : turns == 3 ? -1.0 : 0.0;
                    cosine = turns == 0 ? 1.0 : turns == 2 ? -1.0 : 0.0;
                }
                else
                {
                    position = CapsuleGrid > 0.0
                        ? CapsuleFrameCorner + (CapsulePixel(CapsuleLayerCorner, position, CapsuleGrid) / CapsuleGrid)
                        : CapsuleFrameCorner + (position - CapsuleLayerCorner);
                    if (rotation != 0.0)
                    {
                        sincos(rotation, sine, cosine);
                        turned = true;
                    }
                }
            }

            // A flip swaps the texture coordinates on its axis and leaves the corners in place.
            float2 uvFrom = float2((sprite.Flags & CapsuleFlipX) != 0 ? texBottomRight.x : texTopLeft.x, (sprite.Flags & CapsuleFlipY) != 0 ? texBottomRight.y : texTopLeft.y);
            float2 uvTo = float2((sprite.Flags & CapsuleFlipX) != 0 ? texTopLeft.x : texBottomRight.x, (sprite.Flags & CapsuleFlipY) != 0 ? texTopLeft.y : texBottomRight.y);

            precise float2 drawnOrigin = origin * scale;
            precise float2 drawn = texels * scale;
            precise float2 vertex;
            if (!turned)
            {
                vertex = (position - drawnOrigin) + (drawn * axes);
            }
            else
            {
                precise float2 d = (float2(0.0, 0.0) - drawnOrigin) + (drawn * axes);
                vertex = float2(position.x + (d.x * cosine) - (d.y * sine), position.y + (d.x * sine) + (d.y * cosine));
            }

            CapsuleVertexOutput output;
            output.Position = mul(float4(vertex, 0.0, 1.0), MatrixTransform);
            output.Tint = CapsuleUnpack(sprite.Tint);
            output.Flash = CapsuleUnpack(sprite.Flash);
            output.UV = float2(corner & 1 ? uvTo.x : uvFrom.x, corner >> 1 ? uvTo.y : uvFrom.y);
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
