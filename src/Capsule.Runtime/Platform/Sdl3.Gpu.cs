using System.Runtime.InteropServices;
using Capsule.Runtime.Gpu;

namespace Capsule.Runtime.Platform;

// The GPU device the renderer draws through, and the surface and stream a frame capture encodes its PNG
// with. Every struct here is SDL 3.4's, field for field, its bool fields as bytes. An SDL enum the host
// names is its Capsule.Runtime.Gpu enum, which holds SDL's values.
internal static unsafe partial class Sdl3
{
    // SDL_GPUShaderFormat.
    internal const uint GpuShaderFormatSpirv = 1u << 1;
    internal const uint GpuShaderFormatDxil = 1u << 3;
    internal const uint GpuShaderFormatMsl = 1u << 4;

    // SDL_GPUTextureType, SDL_GPUSampleCount and SDL_GPUPrimitiveType: the first of each.
    internal const int GpuTextureType2D = 0;
    internal const int GpuSampleCount1 = 0;
    internal const int GpuPrimitiveTriangleList = 0;

    // SDL_GPUStoreOp.
    internal const int GpuStoreOpStore = 0;

    // SDL_GPUFillMode, SDL_GPUCullMode and SDL_GPUFrontFace.
    internal const int GpuFillModeFill = 0;
    internal const int GpuCullModeNone = 0;
    internal const int GpuFrontFaceCounterClockwise = 0;

    // SDL_GPUSamplerMipmapMode.
    internal const int GpuMipmapModeNearest = 0;

    // SDL_GPUTransferBufferUsage.
    internal const int GpuTransferUpload = 0;
    internal const int GpuTransferDownload = 1;

    // SDL_GPUPresentMode.
    internal const int GpuPresentVsync = 0;
    internal const int GpuPresentImmediate = 1;
    internal const int GpuPresentMailbox = 2;

    // SDL_GPUSwapchainComposition.
    internal const int GpuCompositionSdr = 0;

    // SDL_IOWhence.
    internal const int IoSeekSet = 0;

    // SDL_PROP_GPU_DEVICE_NAME_STRING, SDL_PROP_GPU_DEVICE_DRIVER_NAME_STRING and
    // SDL_PROP_GPU_DEVICE_DRIVER_VERSION_STRING, null-terminated.
    internal static ReadOnlySpan<byte> GpuDeviceName => "SDL.gpu.device.name\0"u8;

    internal static ReadOnlySpan<byte> GpuDeviceDriverName => "SDL.gpu.device.driver_name\0"u8;

    internal static ReadOnlySpan<byte> GpuDeviceDriverVersion => "SDL.gpu.device.driver_version\0"u8;

    // Picks a driver that reads one of the formats, in SDL's own order unless its SDL_GPU_DRIVER hint
    // names one. Null when none can draw on this machine.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_CreateGPUDevice(uint formats, [MarshalAs(UnmanagedType.U1)] bool debugMode, byte* name);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_DestroyGPUDevice(nint device);

    // The driver's short name, such as "vulkan". SDL owns the string.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetGPUDeviceDriver(nint device);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint SDL_GetGPUShaderFormats(nint device);

    // The drivers this build of SDL holds, whether or not this machine can draw through them.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern int SDL_GetNumGPUDrivers();

    // A driver's short name by index, which SDL owns.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetGPUDriver(int index);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint SDL_GetGPUDeviceProperties(nint device);

    // The property's string, which SDL owns, or defaultValue where it is unset.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetStringProperty(uint properties, byte* name, byte* defaultValue);

    // The window's swapchain starts in SDR composition under VSYNC.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_ClaimWindowForGPUDevice(nint device, nint window);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_ReleaseWindowFromGPUDevice(nint device, nint window);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetGPUAllowedFramesInFlight(nint device, uint frames);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_WindowSupportsGPUPresentMode(nint device, nint window, int presentMode);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetGPUSwapchainParameters(nint device, nint window, int composition, int presentMode);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_WaitForGPUSwapchain(nint device, nint window);

    // Invalid when the window is not claimed.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern GpuTextureFormat SDL_GetGPUSwapchainTextureFormat(nint device, nint window);

    // The SDL_PixelFormat whose bytes the texture format's texels are, or SDL_PIXELFORMAT_UNKNOWN (0).
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint SDL_GetPixelFormatFromGPUTextureFormat(GpuTextureFormat format);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_WaitForGPUIdle(nint device);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_CreateGPUTexture(nint device, GpuTextureCreateInfo* info);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_ReleaseGPUTexture(nint device, nint texture);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_CreateGPUSampler(nint device, GpuSamplerCreateInfo* info);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_ReleaseGPUSampler(nint device, nint sampler);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_CreateGPUBuffer(nint device, GpuBufferCreateInfo* info);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_ReleaseGPUBuffer(nint device, nint buffer);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_CreateGPUTransferBuffer(nint device, GpuTransferBufferCreateInfo* info);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_ReleaseGPUTransferBuffer(nint device, nint transferBuffer);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_CreateGPUShader(nint device, GpuShaderCreateInfo* info);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_ReleaseGPUShader(nint device, nint shader);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_CreateGPUGraphicsPipeline(nint device, GpuGraphicsPipelineCreateInfo* info);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_ReleaseGPUGraphicsPipeline(nint device, nint pipeline);

    // Cycling hands the mapping a fresh store when a command buffer still in flight reads the old one.
    // The buffer is unmapped before a copy pass records an upload from it.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_MapGPUTransferBuffer(nint device, nint transferBuffer, [MarshalAs(UnmanagedType.U1)] bool cycle);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_UnmapGPUTransferBuffer(nint device, nint transferBuffer);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_AcquireGPUCommandBuffer(nint device);

    // Valid in or out of a pass. Every later draw in the command buffer reads the bytes until the next push.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_PushGPUVertexUniformData(nint commandBuffer, uint slot, void* data, uint length);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_PushGPUFragmentUniformData(nint commandBuffer, uint slot, void* data, uint length);

    // Never waits. The texture is null while the window is minimized or the swapchain has none free.
    // Submitting the command buffer presents it, and only that command buffer may draw into it.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_AcquireGPUSwapchainTexture(nint commandBuffer, nint window, nint* texture, uint* width, uint* height);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SubmitGPUCommandBuffer(nint commandBuffer);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_SubmitGPUCommandBufferAndAcquireFence(nint commandBuffer);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_WaitForGPUFences(nint device, [MarshalAs(UnmanagedType.U1)] bool waitAll, nint* fences, uint count);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_ReleaseGPUFence(nint device, nint fence);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_BeginGPUCopyPass(nint commandBuffer);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_UploadToGPUTexture(nint copyPass, GpuTextureTransferInfo* source, GpuTextureRegion* destination, [MarshalAs(UnmanagedType.U1)] bool cycle);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_UploadToGPUBuffer(nint copyPass, GpuTransferBufferLocation* source, GpuBufferRegion* destination, [MarshalAs(UnmanagedType.U1)] bool cycle);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_DownloadFromGPUTexture(nint copyPass, GpuTextureRegion* source, GpuTextureTransferInfo* destination);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_EndGPUCopyPass(nint copyPass);

    // The pass's viewport and scissor start at the whole target.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_BeginGPURenderPass(nint commandBuffer, GpuColorTargetInfo* colorTargets, uint count, void* depthStencilTarget);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_BindGPUGraphicsPipeline(nint renderPass, nint pipeline);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_SetGPUViewport(nint renderPass, GpuViewport* viewport);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_SetGPUScissor(nint renderPass, Rect* scissor);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_BindGPUVertexStorageBuffers(nint renderPass, uint firstSlot, nint* buffers, uint count);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_BindGPUFragmentSamplers(nint renderPass, uint firstSlot, GpuTextureSamplerBinding* bindings, uint count);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_DrawGPUPrimitives(nint renderPass, uint vertices, uint instances, uint firstVertex, uint firstInstance);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_EndGPURenderPass(nint renderPass);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SavePNG_IO(nint surface, nint stream, [MarshalAs(UnmanagedType.U1)] bool closeStream);

    // A stream over memory SDL grows as it is written and frees when the stream closes.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_IOFromDynamicMem();

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern long SDL_GetIOSize(nint stream);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern long SDL_SeekIO(nint stream, long offset, int whence);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nuint SDL_ReadIO(nint stream, void* destination, nuint size);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_CloseIO(nint stream);

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuTextureCreateInfo
    {
        public int Type;
        public GpuTextureFormat Format;
        public GpuTextureUsage Usage;
        public uint Width;
        public uint Height;
        public uint LayerCountOrDepth;
        public uint NumLevels;
        public int SampleCount;
        public uint Props;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuSamplerCreateInfo
    {
        public GpuFilter MinFilter;
        public GpuFilter MagFilter;
        public int MipmapMode;
        public GpuSamplerAddressMode AddressModeU;
        public GpuSamplerAddressMode AddressModeV;
        public GpuSamplerAddressMode AddressModeW;
        public float MipLodBias;
        public float MaxAnisotropy;
        public int CompareOp;
        public float MinLod;
        public float MaxLod;
        public byte EnableAnisotropy;
        public byte EnableCompare;
        public byte Padding1;
        public byte Padding2;
        public uint Props;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuBufferCreateInfo
    {
        public GpuBufferUsage Usage;
        public uint Size;
        public uint Props;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuTransferBufferCreateInfo
    {
        public int Usage;
        public uint Size;
        public uint Props;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuShaderCreateInfo
    {
        public nuint CodeSize;
        public byte* Code;
        public byte* EntryPoint;
        public uint Format;
        public GpuShaderStage Stage;
        public uint NumSamplers;
        public uint NumStorageTextures;
        public uint NumStorageBuffers;
        public uint NumUniformBuffers;
        public uint Props;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuVertexInputState
    {
        public void* VertexBufferDescriptions;
        public uint NumVertexBuffers;
        public void* VertexAttributes;
        public uint NumVertexAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuRasterizerState
    {
        public int FillMode;
        public int CullMode;
        public int FrontFace;
        public float DepthBiasConstantFactor;
        public float DepthBiasClamp;
        public float DepthBiasSlopeFactor;
        public byte EnableDepthBias;
        public byte EnableDepthClip;
        public byte Padding1;
        public byte Padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuMultisampleState
    {
        public int SampleCount;
        public uint SampleMask;
        public byte EnableMask;
        public byte EnableAlphaToCoverage;
        public byte Padding2;
        public byte Padding3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuStencilOpState
    {
        public int FailOp;
        public int PassOp;
        public int DepthFailOp;
        public int CompareOp;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuDepthStencilState
    {
        public int CompareOp;
        public GpuStencilOpState BackStencilState;
        public GpuStencilOpState FrontStencilState;
        public byte CompareMask;
        public byte WriteMask;
        public byte EnableDepthTest;
        public byte EnableDepthWrite;
        public byte EnableStencilTest;
        public byte Padding1;
        public byte Padding2;
        public byte Padding3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuColorTargetBlendState
    {
        public GpuBlendFactor SourceColorBlendFactor;
        public GpuBlendFactor DestinationColorBlendFactor;
        public GpuBlendOp ColorBlendOp;
        public GpuBlendFactor SourceAlphaBlendFactor;
        public GpuBlendFactor DestinationAlphaBlendFactor;
        public GpuBlendOp AlphaBlendOp;
        public byte ColorWriteMask;
        public byte EnableBlend;
        public byte EnableColorWriteMask;
        public byte Padding1;
        public byte Padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuColorTargetDescription
    {
        public GpuTextureFormat Format;
        public GpuColorTargetBlendState BlendState;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuGraphicsPipelineTargetInfo
    {
        public GpuColorTargetDescription* ColorTargetDescriptions;
        public uint NumColorTargets;
        public int DepthStencilFormat;
        public byte HasDepthStencilTarget;
        public byte Padding1;
        public byte Padding2;
        public byte Padding3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuGraphicsPipelineCreateInfo
    {
        public nint VertexShader;
        public nint FragmentShader;
        public GpuVertexInputState VertexInputState;
        public int PrimitiveType;
        public GpuRasterizerState RasterizerState;
        public GpuMultisampleState MultisampleState;
        public GpuDepthStencilState DepthStencilState;
        public GpuGraphicsPipelineTargetInfo TargetInfo;
        public uint Props;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FColor
    {
        public float R;
        public float G;
        public float B;
        public float A;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuColorTargetInfo
    {
        public nint Texture;
        public uint MipLevel;
        public uint LayerOrDepthPlane;
        public FColor ClearColor;
        public GpuLoadOp LoadOp;
        public int StoreOp;
        public nint ResolveTexture;
        public uint ResolveMipLevel;
        public uint ResolveLayer;
        public byte Cycle;
        public byte CycleResolveTexture;
        public byte Padding1;
        public byte Padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuViewport
    {
        public float X;
        public float Y;
        public float W;
        public float H;
        public float MinDepth;
        public float MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int X;
        public int Y;
        public int W;
        public int H;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuTextureTransferInfo
    {
        public nint TransferBuffer;
        public uint Offset;
        public uint PixelsPerRow;
        public uint RowsPerLayer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuTransferBufferLocation
    {
        public nint TransferBuffer;
        public uint Offset;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuTextureRegion
    {
        public nint Texture;
        public uint MipLevel;
        public uint Layer;
        public uint X;
        public uint Y;
        public uint Z;
        public uint W;
        public uint H;
        public uint D;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuBufferRegion
    {
        public nint Buffer;
        public uint Offset;
        public uint Size;
    }
}
