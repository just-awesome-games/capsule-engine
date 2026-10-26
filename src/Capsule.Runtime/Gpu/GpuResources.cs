using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Gpu;

// SDL_GPUTextureFormat, as far as the host creates textures. Each value is SDL's.
internal enum GpuTextureFormat
{
    R8Unorm = 2,
    R8G8B8A8Unorm = 4,
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

// SDL_GPUShaderCreateInfo. Name is what a failure to create it reports.
internal sealed record GpuShaderInfo(
    string Name,
    ReadOnlyMemory<byte> Code,
    string EntryPoint,
    GpuShaderStage Stage,
    int Samplers,
    int StorageTextures,
    int StorageBuffers,
    int UniformBuffers);

// SDL_GPUGraphicsPipelineCreateInfo for a triangle list drawn into one color target, culling nothing.
internal readonly record struct GpuPipelineInfo(GpuShader VertexShader, GpuShader FragmentShader, GpuTextureFormat Target, GpuBlendState Blend);

// SDL_GPUTexture. Released once no submitted command buffer still uses it.
internal sealed class GpuTexture(nint device, nint handle, GpuTextureFormat format, int width, int height) : IDisposable
{
    internal nint Handle { get; private set; } = handle;

    internal GpuTextureFormat Format { get; } = format;

    internal int Width { get; } = width;

    internal int Height { get; } = height;

    public void Dispose()
    {
        if (Handle != 0)
        {
            Sdl3.SDL_ReleaseGPUTexture(device, Handle);
            Handle = 0;
        }
    }
}

// SDL_GPUSampler. The device that created it releases it.
internal sealed class GpuSampler(nint handle)
{
    internal nint Handle { get; } = handle;
}

// SDL_GPUBuffer. Released once no submitted command buffer still uses it.
internal sealed class GpuBuffer(nint device, nint handle, GpuBufferUsage usage, int size) : IDisposable
{
    internal nint Handle { get; private set; } = handle;

    internal GpuBufferUsage Usage { get; } = usage;

    internal int Size { get; } = size;

    public void Dispose()
    {
        if (Handle != 0)
        {
            Sdl3.SDL_ReleaseGPUBuffer(device, Handle);
            Handle = 0;
        }
    }
}

// SDL_GPUShader. A pipeline created from it keeps working once it is released.
internal sealed class GpuShader(nint device, nint handle, GpuShaderInfo info) : IDisposable
{
    internal nint Handle { get; private set; } = handle;

    internal GpuShaderInfo Info { get; } = info;

    public void Dispose()
    {
        if (Handle != 0)
        {
            Sdl3.SDL_ReleaseGPUShader(device, Handle);
            Handle = 0;
        }
    }
}

// SDL_GPUGraphicsPipeline.
internal sealed class GpuPipeline(nint device, nint handle, GpuPipelineInfo info) : IDisposable
{
    internal nint Handle { get; private set; } = handle;

    internal GpuPipelineInfo Info { get; } = info;

    public void Dispose()
    {
        if (Handle != 0)
        {
            Sdl3.SDL_ReleaseGPUGraphicsPipeline(device, Handle);
            Handle = 0;
        }
    }
}

// SDL_GPUTransferBuffer for one download: the RGBA texels of a Width by Height texture, rows tightly
// packed, readable once the command buffer that recorded the download completes.
internal sealed class GpuReadback(nint device, nint handle, int width, int height) : IDisposable
{
    internal nint Handle { get; private set; } = handle;

    internal int Width { get; } = width;

    internal int Height { get; } = height;

    // The texels as a PNG, through SDL's encoder.
    internal unsafe byte[] EncodePng()
    {
        byte* texels = Sdl3.SDL_MapGPUTransferBuffer(device, Handle, cycle: false);
        if (texels is null)
        {
            throw new InvalidOperationException($"The captured frame could not be read back: {Sdl3.Error()}");
        }

        nint surface = 0;
        nint stream = 0;
        try
        {
            surface = Sdl3.SDL_CreateSurfaceFrom(Width, Height, Sdl3.PixelFormatRgba32, texels, Width * 4);
            stream = Sdl3.SDL_IOFromDynamicMem();
            if (surface == 0 || stream == 0 || !Sdl3.SDL_SavePNG_IO(surface, stream, closeStream: false))
            {
                throw new InvalidOperationException($"The captured frame could not be encoded as a PNG: {Sdl3.Error()}");
            }

            byte[] png = new byte[checked((int)Sdl3.SDL_GetIOSize(stream))];
            Sdl3.SDL_SeekIO(stream, 0, Sdl3.IoSeekSet);
            fixed (byte* into = png)
            {
                if (Sdl3.SDL_ReadIO(stream, into, (nuint)png.Length) != (nuint)png.Length)
                {
                    throw new InvalidOperationException($"The captured frame's PNG could not be read back: {Sdl3.Error()}");
                }
            }

            return png;
        }
        finally
        {
            if (stream != 0)
            {
                Sdl3.SDL_CloseIO(stream);
            }

            if (surface != 0)
            {
                Sdl3.SDL_DestroySurface(surface);
            }

            Sdl3.SDL_UnmapGPUTransferBuffer(device, Handle);
        }
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            Sdl3.SDL_ReleaseGPUTransferBuffer(device, Handle);
            Handle = 0;
        }
    }
}
