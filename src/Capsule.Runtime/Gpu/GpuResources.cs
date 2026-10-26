using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Gpu;

// SDL_GPUTextureFormat, as far as the host creates textures.
internal enum GpuTextureFormat
{
    R8G8B8A8Unorm,
    R8Unorm,
}

// SDL_GPUTextureUsageFlags.
[Flags]
internal enum GpuTextureUsage
{
    Sampler = 1,
    ColorTarget = 2,
}

// SDL_GPUBufferUsageFlags.
[Flags]
internal enum GpuBufferUsage
{
    Vertex = 1,
    Index = 2,
    GraphicsStorageRead = 8,
}

// SDL_GPUShaderStage.
internal enum GpuShaderStage
{
    Vertex,
    Fragment,
}

// SDL_GPUFilter.
internal enum GpuFilter
{
    Nearest,
    Linear,
}

// SDL_GPUSamplerAddressMode.
internal enum GpuSamplerAddressMode
{
    Repeat,
    MirroredRepeat,
    ClampToEdge,
}

// SDL_GPUBlendFactor.
internal enum GpuBlendFactor
{
    Zero = 1,
    One,
    SourceColor,
    OneMinusSourceColor,
    DestinationColor,
    OneMinusDestinationColor,
    SourceAlpha,
    OneMinusSourceAlpha,
    DestinationAlpha,
    OneMinusDestinationAlpha,
    ConstantColor,
    OneMinusConstantColor,
    SourceAlphaSaturate,
}

// SDL_GPUBlendOp.
internal enum GpuBlendOp
{
    Add = 1,
    Subtract,
    ReverseSubtract,
    Min,
    Max,
}

// SDL_GPUColorTargetBlendState with blending on and every channel written.
internal readonly record struct GpuBlendState(
    GpuBlendFactor SourceColor,
    GpuBlendFactor DestinationColor,
    GpuBlendOp ColorOp,
    GpuBlendFactor SourceAlpha,
    GpuBlendFactor DestinationAlpha,
    GpuBlendOp AlphaOp);

// SDL_GPUTextureCreateInfo for a 2D texture of one level and one layer.
internal readonly record struct GpuTextureInfo(GpuTextureFormat Format, GpuTextureUsage Usage, int Width, int Height);

// SDL_GPUSamplerCreateInfo with no mipmaps, anisotropy or comparison.
internal readonly record struct GpuSamplerInfo(GpuFilter Filter, GpuSamplerAddressMode AddressMode);

// SDL_GPUShaderCreateInfo, with the name SDL takes as a property. Effect is what the OpenGL backend
// sets the effect's parameters by, and null for a vertex shader.
internal sealed record GpuShaderInfo(
    string Name,
    ReadOnlyMemory<byte> Code,
    string EntryPoint,
    GpuShaderStage Stage,
    int Samplers,
    int StorageTextures,
    int StorageBuffers,
    int UniformBuffers,
    GpuEffectBindings? Effect = null);

// How the OpenGL effect of a fragment shader is reached from SDL's binding layout. Each uniform names
// the effect parameter written from Floats floats at Offset bytes into uniform buffer Buffer. Textures
// names the parameter at each sampler slot, null for slot 0, whose texture the device binds itself.
// Sizes names the parameter that slot's texture size is written to, null where the effect reads none.
internal sealed record GpuEffectBindings(GpuEffectUniform[] Uniforms, string?[] Textures, string?[] Sizes);

internal readonly record struct GpuEffectUniform(string Name, int Buffer, int Offset, int Floats);

// SDL_GPUGraphicsPipelineCreateInfo for a triangle list drawn into one color target.
internal readonly record struct GpuPipelineInfo(GpuShader VertexShader, GpuShader FragmentShader, GpuTextureFormat Target, GpuBlendState Blend);

// SDL_GPUTexture. The back buffer's texture is the device's, and is never disposed by its user.
internal sealed class GpuTexture(Texture2D? body, GpuTextureFormat format, int width, int height) : IDisposable
{
    internal GpuTextureFormat Format { get; } = format;

    internal int Width { get; set; } = width;

    internal int Height { get; set; } = height;

    // Null for the back buffer.
    internal Texture2D? Body { get; } = body;

    public void Dispose() => Body?.Dispose();
}

// SDL_GPUSampler.
internal sealed class GpuSampler(SamplerState body)
{
    internal SamplerState Body { get; } = body;
}

// SDL_GPUBuffer. The OpenGL backend reads it on the CPU, and its body is the bytes last uploaded.
internal sealed class GpuBuffer(GpuBufferUsage usage, int size)
{
    internal GpuBufferUsage Usage { get; } = usage;

    internal int Size { get; } = size;

    internal byte[] Body { get; } = new byte[size];
}

// SDL_GPUShader. A vertex shader has no OpenGL body: each fragment shader's effect carries its own
// vertex stage. The parameters are the effect's, resolved once in the order Info.Effect names them.
internal sealed class GpuShader(GpuShaderInfo info) : IDisposable
{
    internal GpuShaderInfo Info { get; } = info;

    internal Effect? Effect { get; init; }

    internal EffectParameter[] Uniforms { get; init; } = [];

    internal EffectParameter?[] Textures { get; init; } = [];

    internal EffectParameter?[] Sizes { get; init; } = [];

    public void Dispose() => Effect?.Dispose();
}

// SDL_GPUGraphicsPipeline.
internal sealed class GpuPipeline(GpuPipelineInfo info, BlendState blend)
{
    internal GpuPipelineInfo Info { get; } = info;

    internal BlendState Blend { get; } = blend;
}

// SDL_GPUTransferBuffer for downloads: RGBA texels a copy pass reads back, readable once the command
// buffer that recorded the download completes.
internal sealed class GpuReadback(int size)
{
    internal byte[] Body { get; } = new byte[size];

    internal int Width { get; set; }

    internal int Height { get; set; }
}
