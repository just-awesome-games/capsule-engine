using System.Diagnostics;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Gpu;

// SDL_GPUTextureFormat, as far as the host creates textures and the swapchain takes them. Each value is
// SDL's.
internal enum GpuTextureFormat
{
    Invalid = 0,
    R8Unorm = 2,
    R8G8B8A8Unorm = 4,
    B8G8R8A8Unorm = 12,
}

// SDL_GPUTextureUsageFlags.
[Flags]
internal enum GpuTextureUsage : uint
{
    Sampler = 1,
    ColorTarget = 2,
}

// SDL_GPUBufferUsageFlags.
[Flags]
internal enum GpuBufferUsage : uint
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

// SDL_GPUTexture. Released once no submitted command buffer still uses it. A device of 0 marks a
// texture SDL owns, the swapchain's, which Dispose leaves alone.
internal sealed class GpuTexture(nint device, nint handle, GpuTextureFormat format, int width, int height) : IDisposable
{
    internal nint Handle { get; private set; } = handle;

    internal GpuTextureFormat Format { get; } = format;

    internal int Width { get; private set; } = width;

    internal int Height { get; private set; } = height;

    // Points a texture SDL owns at the one a frame acquired, which only that frame may draw into.
    internal void Borrow(nint handle, int width, int height)
    {
        Debug.Assert(device == 0, "Only a texture SDL owns is re-pointed.");
        Handle = handle;
        Width = width;
        Height = height;
    }

    public void Dispose()
    {
        if (Handle != 0 && device != 0)
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

// SDL_GPUTransferBuffer for one download: the texels of a Width by Height texture of four bytes each in
// Format, rows tightly packed, readable once the command buffer that recorded the download completes.
internal sealed class GpuReadback(nint device, nint handle, int width, int height, GpuTextureFormat format) : IDisposable
{
    internal nint Handle { get; private set; } = handle;

    internal int Width { get; } = width;

    internal int Height { get; } = height;

    internal GpuTextureFormat Format { get; } = format;

    // The texels as a PNG.
    internal unsafe byte[] EncodePng()
    {
        byte* texels = Sdl3.SDL_MapGPUTransferBuffer(device, Handle, cycle: false);
        if (texels is null)
        {
            throw new InvalidOperationException($"The captured frame could not be read back: {Sdl3.Error()}");
        }

        try
        {
            return EncodePng(new ReadOnlySpan<byte>(texels, Width * Height * 4), Width, Height, Format);
        }
        finally
        {
            Sdl3.SDL_UnmapGPUTransferBuffer(device, Handle);
        }
    }

    // Texels of four bytes each in format, rows tightly packed, as a PNG through SDL's encoder, which
    // reads them in the pixel format matching format. The PNG is the same whichever byte order holds the
    // channels.
    internal static unsafe byte[] EncodePng(ReadOnlySpan<byte> texels, int width, int height, GpuTextureFormat format)
    {
        uint pixels = Sdl3.SDL_GetPixelFormatFromGPUTextureFormat(format);
        if (pixels == 0)
        {
            throw new InvalidOperationException($"The captured frame's texture format {format} has no pixel format to encode a PNG from.");
        }

        nint surface = 0;
        nint stream = 0;
        try
        {
            fixed (byte* from = texels)
            {
                surface = Sdl3.SDL_CreateSurfaceFrom(width, height, pixels, from, width * 4);
                stream = Sdl3.SDL_IOFromDynamicMem();
                if (surface == 0 || stream == 0 || !Sdl3.SDL_SavePNG_IO(surface, stream, closeStream: false))
                {
                    throw new InvalidOperationException($"The captured frame could not be encoded as a PNG: {Sdl3.Error()}");
                }
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
