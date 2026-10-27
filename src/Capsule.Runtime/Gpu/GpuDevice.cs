using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Capsule.Rendering;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Gpu;

// SDL_GPUDevice and the window it presents to: creates the GPU resources and the command buffers that
// use them, with the frames in flight the game allows. A copy pass stages every upload in one transfer buffer the device
// keeps, appended from the start of each frame, and records the uploads when the pass ends, after the
// buffer is unmapped. The first staging of a frame cycles the buffer, which never waits on a frame
// still in flight. A burst of uploads overflows the buffer into further copies, a buffer upload
// larger than it grows it, and StagingSettle returns it to its steady size once frames are quiet
// again. A frame at steady state allocates nothing.
internal sealed unsafe class GpuDevice : IDisposable
{
    // Every staged upload starts on this boundary, a multiple of any texel's size.
    private const int StagingAlignment = 16;

    private readonly nint _window;
    private readonly nint _device;
    private readonly uint _shaderFormat;

    // Every sampler this device created, released with it. A run creates a few and keeps them.
    private readonly List<GpuSampler> _samplers = [];

    // How long frames stay within the steady size before a grown or overflowed buffer is recreated.
    private static readonly long QuietTicks = 2 * Stopwatch.Frequency;

    private StagingSettle _settle = new(QuietTicks);

    private nint _staging;
    private int _stagingSize;

    // Bytes the frame has staged across every buffer it filled, and whether it grew or overflowed one.
    private int _frameStaged;
    private bool _overflowed;

    // Bytes of the staging buffer this frame has used, and its mapping while a copy pass writes to it.
    private int _stagingUsed;
    private byte* _mapped;

    // The copy pass's uploads and downloads in the order recorded, encoded when it ends.
    private Transfer[] _transfers = new Transfer[64];
    private int _transferCount;

    // The shader format whose files this device creates shaders from: spirv, dxil or msl.
    internal string ShaderFormat { get; }

    // One line for the development log: the driver, the device and its driver version.
    internal string Description { get; }

    // Creates SDL's GPU device through the named driver, or the one SDL chooses when driver is null, and
    // claims the window for it in SDR composition under VSYNC.
    internal GpuDevice(nint window, string? driver, int framesInFlight)
    {
        _window = window;
        fixed (byte* name = driver is null ? null : Encoding.UTF8.GetBytes(driver + '\0'))
        {
            _device = Sdl3.SDL_CreateGPUDevice(Sdl3.GpuShaderFormatSpirv | Sdl3.GpuShaderFormatDxil | Sdl3.GpuShaderFormatMsl, debugMode: false, name);
        }

        if (_device == 0)
        {
            throw driver is null ? Unavailable("could not open a GPU") : new InvalidOperationException(DriverRefused(driver, Sdl3.Error(), OfferedDrivers()));
        }

        if (!Sdl3.SDL_ClaimWindowForGPUDevice(_device, window))
        {
            InvalidOperationException refused = new(ClaimRefused(Utf8(Sdl3.SDL_GetGPUDeviceDriver(_device)) ?? "unnamed", Sdl3.Error()));
            Sdl3.SDL_DestroyGPUDevice(_device);
            throw refused;
        }

        Sdl3.SDL_SetGPUAllowedFramesInFlight(_device, (uint)framesInFlight);

        uint formats = Sdl3.SDL_GetGPUShaderFormats(_device);
        (_shaderFormat, ShaderFormat) = (formats & Sdl3.GpuShaderFormatDxil) != 0 ? (Sdl3.GpuShaderFormatDxil, "dxil")
            : (formats & Sdl3.GpuShaderFormatMsl) != 0 ? (Sdl3.GpuShaderFormatMsl, "msl")
            : (Sdl3.GpuShaderFormatSpirv, "spirv");
        Description = Describe(_device);
        _stagingSize = StagingSettle.SteadyBytes;
        _staging = CreateTransferBuffer(Sdl3.GpuTransferUpload, _stagingSize);
    }

    // The window's drawable extent in pixels, zero while it has none.
    internal (int Width, int Height) WindowSize
    {
        get
        {
            int width;
            int height;

            return Sdl3.SDL_GetWindowSizeInPixels(_window, &width, &height) ? (width, height) : (0, 0);
        }
    }

    // Presents in the nearest mode the GPU has to requested and returns it. Off falls back to Mailbox,
    // and Mailbox to On, which every GPU has.
    internal VSyncMode ApplyVSync(VSyncMode requested)
    {
        if (requested == VSyncMode.Off && TryPresentMode(Sdl3.GpuPresentImmediate))
        {
            return VSyncMode.Off;
        }

        if (requested != VSyncMode.On && TryPresentMode(Sdl3.GpuPresentMailbox))
        {
            return VSyncMode.Mailbox;
        }

        if (!Sdl3.SDL_SetGPUSwapchainParameters(_device, _window, Sdl3.GpuCompositionSdr, Sdl3.GpuPresentVsync))
        {
            throw new InvalidOperationException($"The window's swapchain refused VSYNC, which every GPU presents with: {Sdl3.Error()}");
        }

        return VSyncMode.On;
    }

    // Returns once the next swapchain texture can be acquired. A failed wait leaves the next acquire to
    // find no texture and present nothing.
    internal void WaitForSwapchain() => Sdl3.SDL_WaitForGPUSwapchain(_device, _window);

    internal GpuCommandBuffer AcquireCommandBuffer()
    {
        nint commands = Sdl3.SDL_AcquireGPUCommandBuffer(_device);

        return commands != 0
            ? new GpuCommandBuffer(this, commands)
            : throw new InvalidOperationException($"The GPU gave no command buffer: {Sdl3.Error()}");
    }

    internal GpuTexture CreateTexture(in GpuTextureInfo info)
    {
        Sdl3.GpuTextureCreateInfo create = new()
        {
            Type = Sdl3.GpuTextureType2D,
            Format = (int)info.Format,
            Usage = (uint)info.Usage,
            Width = (uint)info.Width,
            Height = (uint)info.Height,
            LayerCountOrDepth = 1,
            NumLevels = 1,
            SampleCount = Sdl3.GpuSampleCount1,
        };
        nint texture = Sdl3.SDL_CreateGPUTexture(_device, &create);

        return texture != 0
            ? new GpuTexture(_device, texture, info.Format, info.Width, info.Height)
            : throw new InvalidOperationException($"A {info.Width} by {info.Height} texture could not be created: {Sdl3.Error()}");
    }

    internal GpuSampler CreateSampler(in GpuSamplerInfo info)
    {
        Sdl3.GpuSamplerCreateInfo create = new()
        {
            MinFilter = (int)info.Filter,
            MagFilter = (int)info.Filter,
            MipmapMode = Sdl3.GpuMipmapModeNearest,
            AddressModeU = (int)info.AddressMode,
            AddressModeV = (int)info.AddressMode,
            AddressModeW = (int)info.AddressMode,
        };
        nint sampler = Sdl3.SDL_CreateGPUSampler(_device, &create);
        if (sampler == 0)
        {
            throw new InvalidOperationException($"A sampler could not be created: {Sdl3.Error()}");
        }

        GpuSampler created = new(sampler);
        _samplers.Add(created);

        return created;
    }

    internal GpuBuffer CreateBuffer(GpuBufferUsage usage, int size)
    {
        Sdl3.GpuBufferCreateInfo create = new() { Usage = (uint)usage, Size = (uint)size };
        nint buffer = Sdl3.SDL_CreateGPUBuffer(_device, &create);

        return buffer != 0
            ? new GpuBuffer(_device, buffer, usage, size)
            : throw new InvalidOperationException($"A buffer of {size} bytes could not be created: {Sdl3.Error()}");
    }

    // A download of a width by height RGBA texture.
    internal GpuReadback CreateReadback(int width, int height) =>
        new(_device, CreateTransferBuffer(Sdl3.GpuTransferDownload, width * height * 4), width, height);

    internal GpuShader CreateShader(GpuShaderInfo info)
    {
        byte[] entryPoint = Encoding.UTF8.GetBytes(info.EntryPoint + "\0");
        nint shader;
        fixed (byte* code = info.Code.Span)
        fixed (byte* entry = entryPoint)
        {
            Sdl3.GpuShaderCreateInfo create = new()
            {
                CodeSize = (nuint)info.Code.Length,
                Code = code,
                EntryPoint = entry,
                Format = _shaderFormat,
                Stage = (int)info.Stage,
                NumSamplers = (uint)info.Samplers,
                NumStorageTextures = (uint)info.StorageTextures,
                NumStorageBuffers = (uint)info.StorageBuffers,
                NumUniformBuffers = (uint)info.UniformBuffers,
            };
            shader = Sdl3.SDL_CreateGPUShader(_device, &create);
        }

        return shader != 0
            ? new GpuShader(_device, shader, info)
            : throw new InvalidOperationException($"Shader '{info.Name}' could not be created from its {ShaderFormat} code: {Sdl3.Error()}. Rebuild the game so the shipped shader and the engine agree.");
    }

    // The driver compiles the pipeline's program here, never on its first draw.
    internal GpuPipeline CreatePipeline(in GpuPipelineInfo info)
    {
        GpuBlendState blend = info.Blend;
        Sdl3.GpuColorTargetDescription target = new()
        {
            Format = (int)info.Target,
            BlendState = new Sdl3.GpuColorTargetBlendState
            {
                SourceColorBlendFactor = (int)blend.SourceColor,
                DestinationColorBlendFactor = (int)blend.DestinationColor,
                ColorBlendOp = (int)blend.ColorOp,
                SourceAlphaBlendFactor = (int)blend.SourceAlpha,
                DestinationAlphaBlendFactor = (int)blend.DestinationAlpha,
                AlphaBlendOp = (int)blend.AlphaOp,
                EnableBlend = 1,
            },
        };
        Sdl3.GpuGraphicsPipelineCreateInfo create = new()
        {
            VertexShader = info.VertexShader.Handle,
            FragmentShader = info.FragmentShader.Handle,
            PrimitiveType = Sdl3.GpuPrimitiveTriangleList,
            RasterizerState = new Sdl3.GpuRasterizerState
            {
                FillMode = Sdl3.GpuFillModeFill,
                CullMode = Sdl3.GpuCullModeNone,
                FrontFace = Sdl3.GpuFrontFaceCounterClockwise,
            },
            MultisampleState = new Sdl3.GpuMultisampleState { SampleCount = Sdl3.GpuSampleCount1 },
            TargetInfo = new Sdl3.GpuGraphicsPipelineTargetInfo { ColorTargetDescriptions = &target, NumColorTargets = 1 },
        };
        nint pipeline = Sdl3.SDL_CreateGPUGraphicsPipeline(_device, &create);

        return pipeline != 0
            ? new GpuPipeline(_device, pipeline, info)
            : throw new InvalidOperationException($"Shader '{info.FragmentShader.Info.Name}' could not be linked into a pipeline: {Sdl3.Error()}");
    }

    // Waits for the GPU to go idle, releases what the device created, releases the window and destroys
    // the device. The window outlives it.
    public void Dispose()
    {
        Sdl3.SDL_WaitForGPUIdle(_device);
        Sdl3.SDL_ReleaseGPUTransferBuffer(_device, _staging);
        foreach (GpuSampler sampler in _samplers)
        {
            Sdl3.SDL_ReleaseGPUSampler(_device, sampler.Handle);
        }

        Sdl3.SDL_ReleaseWindowFromGPUDevice(_device, _window);
        Sdl3.SDL_DestroyGPUDevice(_device);
    }

    // Blits surface over the window's swapchain texture, unless the window shows none this frame.
    // Neither waits.
    internal void Present(nint commands, GpuTexture? surface)
    {
        nint swapchain;
        uint width;
        uint height;
        if (!Sdl3.SDL_AcquireGPUSwapchainTexture(commands, _window, &swapchain, &width, &height))
        {
            throw new InvalidOperationException($"The window's swapchain texture could not be acquired: {Sdl3.Error()}");
        }

        if (swapchain == 0 || surface is null)
        {
            return;
        }

        Sdl3.GpuBlitInfo blit = new()
        {
            Source = new Sdl3.GpuBlitRegion { Texture = surface.Handle, W = (uint)surface.Width, H = (uint)surface.Height },
            Destination = new Sdl3.GpuBlitRegion { Texture = swapchain, W = width, H = height },
            LoadOp = Sdl3.GpuLoadOpDontCare,
            FlipMode = Sdl3.FlipNone,
            Filter = (int)GpuFilter.Nearest,
        };
        Sdl3.SDL_BlitGPUTexture(commands, &blit);
    }

    internal void Submit(nint commands)
    {
        if (!Sdl3.SDL_SubmitGPUCommandBuffer(commands))
        {
            throw new InvalidOperationException($"The GPU refused the frame: {Sdl3.Error()}");
        }

        EndFrame();
    }

    // Submits and blocks until the GPU has run the command buffer.
    internal void SubmitAndWait(nint commands)
    {
        nint fence = Sdl3.SDL_SubmitGPUCommandBufferAndAcquireFence(commands);
        if (fence == 0)
        {
            throw new InvalidOperationException($"The GPU refused the frame: {Sdl3.Error()}");
        }

        Sdl3.SDL_WaitForGPUFences(_device, waitAll: true, &fence, 1);
        Sdl3.SDL_ReleaseGPUFence(_device, fence);
        EndFrame();
    }

    // Stages the texels in bands of rows that fit the steady staging size. Only a single row larger
    // than that grows the buffer, and a band overflowing it continues in a fresh copy of it.
    internal void UploadToTexture(nint copyPass, GpuTexture texture, int x, int y, int width, int height, ReadOnlySpan<byte> texels)
    {
        if (height == 0)
        {
            return;
        }

        int rowBytes = texels.Length / height;
        int bandRows = StagingSettle.BandRows(rowBytes);
        for (int row = 0; row < height; row += bandRows)
        {
            int rows = Math.Min(bandRows, height - row);
            int offset = Stage(copyPass, texels.Slice(row * rowBytes, rows * rowBytes));
            Record(new Transfer(TransferKind.Texture, texture.Handle, offset, x, y + row, width, rows, Cycle: false));
        }
    }

    internal void UploadToBuffer(nint copyPass, GpuBuffer buffer, int offset, ReadOnlySpan<byte> data, bool cycle)
    {
        if (data.IsEmpty)
        {
            return;
        }

        int staged = Stage(copyPass, data);
        Record(new Transfer(TransferKind.Buffer, buffer.Handle, staged, offset, 0, data.Length, 0, cycle));
    }

    internal void DownloadFromTexture(GpuTexture texture, GpuReadback readback) =>
        Record(new Transfer(TransferKind.Download, texture.Handle, 0, 0, 0, texture.Width, texture.Height, false, readback.Handle));

    // Unmaps the staging buffer, records the pass's transfers in order, and ends it.
    internal void EndCopyPass(nint copyPass)
    {
        Flush(copyPass);
        Sdl3.SDL_EndGPUCopyPass(copyPass);
    }

    // The present mode, applied where the window supports it.
    private bool TryPresentMode(int mode) =>
        Sdl3.SDL_WindowSupportsGPUPresentMode(_device, _window, mode)
        && Sdl3.SDL_SetGPUSwapchainParameters(_device, _window, Sdl3.GpuCompositionSdr, mode);

    // Copies data into the staging buffer and returns its offset there. Data that does not fit records
    // what is staged so far and continues in a fresh store, grown when data alone outgrows it.
    private int Stage(nint copyPass, ReadOnlySpan<byte> data)
    {
        _frameStaged += data.Length;
        int offset = (_stagingUsed + StagingAlignment - 1) & ~(StagingAlignment - 1);
        if (offset + data.Length > _stagingSize)
        {
            _overflowed = true;
            Flush(copyPass);
            offset = 0;
            if (data.Length > _stagingSize)
            {
                Sdl3.SDL_ReleaseGPUTransferBuffer(_device, _staging);
                _stagingSize = Math.Max(_stagingSize * 2, (data.Length + StagingAlignment - 1) & ~(StagingAlignment - 1));
                _staging = CreateTransferBuffer(Sdl3.GpuTransferUpload, _stagingSize);
            }
        }

        if (_mapped is null)
        {
            // Cycled at a frame's first staging and after an overflow. Later stagings of a frame append to
            // what its earlier copy passes recorded.
            _mapped = Sdl3.SDL_MapGPUTransferBuffer(_device, _staging, cycle: offset == 0);
            if (_mapped is null)
            {
                throw new InvalidOperationException($"The staging buffer could not be mapped: {Sdl3.Error()}");
            }
        }

        data.CopyTo(new Span<byte>(_mapped + offset, data.Length));
        _stagingUsed = offset + data.Length;

        return offset;
    }

    // Starts the next frame's staging from the buffer's start. A submitted frame keeps its copy of the
    // buffer until the GPU has run it. Recreating the buffer here never waits.
    private void EndFrame()
    {
        bool settle = _settle.EndFrame(_frameStaged, _overflowed, Stopwatch.GetTimestamp());
        _stagingUsed = 0;
        _frameStaged = 0;
        _overflowed = false;
        if (settle)
        {
            Sdl3.SDL_ReleaseGPUTransferBuffer(_device, _staging);
            _stagingSize = StagingSettle.SteadyBytes;
            _staging = CreateTransferBuffer(Sdl3.GpuTransferUpload, _stagingSize);
        }
    }

    private void Record(in Transfer transfer)
    {
        if (_transferCount == _transfers.Length)
        {
            Array.Resize(ref _transfers, _transfers.Length * 2);
        }

        _transfers[_transferCount++] = transfer;
    }

    private void Flush(nint copyPass)
    {
        if (_mapped is not null)
        {
            Sdl3.SDL_UnmapGPUTransferBuffer(_device, _staging);
            _mapped = null;
        }

        for (int i = 0; i < _transferCount; i++)
        {
            ref readonly Transfer transfer = ref _transfers[i];
            switch (transfer.Kind)
            {
                case TransferKind.Texture:
                    {
                        Sdl3.GpuTextureTransferInfo source = new() { TransferBuffer = _staging, Offset = (uint)transfer.Offset, PixelsPerRow = (uint)transfer.Width };
                        Sdl3.GpuTextureRegion destination = Region(transfer);
                        Sdl3.SDL_UploadToGPUTexture(copyPass, &source, &destination, transfer.Cycle);
                        break;
                    }

                case TransferKind.Buffer:
                    {
                        Sdl3.GpuTransferBufferLocation source = new() { TransferBuffer = _staging, Offset = (uint)transfer.Offset };
                        Sdl3.GpuBufferRegion destination = new() { Buffer = transfer.Target, Offset = (uint)transfer.X, Size = (uint)transfer.Width };
                        Sdl3.SDL_UploadToGPUBuffer(copyPass, &source, &destination, transfer.Cycle);
                        break;
                    }

                default:
                    {
                        Sdl3.GpuTextureRegion source = Region(transfer);
                        Sdl3.GpuTextureTransferInfo destination = new() { TransferBuffer = transfer.Readback };
                        Sdl3.SDL_DownloadFromGPUTexture(copyPass, &source, &destination);
                        break;
                    }
            }
        }

        _transferCount = 0;
    }

    private static Sdl3.GpuTextureRegion Region(in Transfer transfer) => new()
    {
        Texture = transfer.Target,
        X = (uint)transfer.X,
        Y = (uint)transfer.Y,
        W = (uint)transfer.Width,
        H = (uint)transfer.Height,
        D = 1,
    };

    private nint CreateTransferBuffer(int usage, int size)
    {
        Sdl3.GpuTransferBufferCreateInfo create = new() { Usage = usage, Size = (uint)size };
        nint buffer = Sdl3.SDL_CreateGPUTransferBuffer(_device, &create);

        return buffer != 0
            ? buffer
            : throw new InvalidOperationException($"A transfer buffer of {size} bytes could not be created: {Sdl3.Error()}");
    }

    // A device that opened and then could not present to the window is no GPU shortfall. Its message
    // sends the reader to the display and the driver choice, not to a driver update.
    internal static string ClaimRefused(string driver, string error) =>
        $"Capsule's {driver} GPU could not present to the window: {error}. A driver can refuse while every display is off: turn a display on, or run with --gpu-driver naming another driver (direct3d12, vulkan or metal).";

    // A driver the player named that this machine or this build cannot draw through. Its message names
    // the drivers the build holds, and sends the reader back to the platform's own choice.
    internal static string DriverRefused(string driver, string error, string offered) =>
        $"Capsule could not draw through the GPU driver '{driver}' that --gpu-driver named: {error}. This build holds {offered}. Name another, or leave the flag out to let Capsule choose.";

    private static string OfferedDrivers()
    {
        int count = Sdl3.SDL_GetNumGPUDrivers();
        string[] names = new string[Math.Max(count, 0)];
        for (int index = 0; index < names.Length; index++)
        {
            names[index] = Utf8(Sdl3.SDL_GetGPUDriver(index)) ?? "an unnamed driver";
        }

        return names.Length == 0 ? "no GPU driver" : string.Join(", ", names);
    }

    private static InvalidOperationException Unavailable(string failure) => new(
        $"Capsule {failure}: {Sdl3.Error()}. It draws through Direct3D 12 at feature level 11_0, Vulkan 1.0, or Metal on a GPU of family Mac2. Update the graphics driver, or run on a machine whose GPU has one of them.");

    private static string Describe(nint device)
    {
        uint properties = Sdl3.SDL_GetGPUDeviceProperties(device);
        string driver = Utf8(Sdl3.SDL_GetGPUDeviceDriver(device)) ?? "an unnamed driver";
        string? name = Property(properties, Sdl3.GpuDeviceName);
        string? vendor = Property(properties, Sdl3.GpuDeviceDriverName);
        string? version = Property(properties, Sdl3.GpuDeviceDriverVersion);
        string described = name is null ? driver : $"{driver} on {name}";
        string driverInfo = string.Join(' ', ((string?[])[vendor, version]).OfType<string>());

        return driverInfo.Length == 0 ? described : $"{described}, driver {driverInfo}";
    }

    private static string? Property(uint properties, ReadOnlySpan<byte> name)
    {
        fixed (byte* key = name)
        {
            return properties == 0 ? null : Utf8(Sdl3.SDL_GetStringProperty(properties, key, null));
        }
    }

    private static string? Utf8(byte* text) => text is null ? null : Marshal.PtrToStringUTF8((nint)text) is { Length: > 0 } value ? value : null;

    private enum TransferKind
    {
        Texture,
        Buffer,
        Download,
    }

    // One recorded transfer. A texture's region is X, Y, Width by Height, an upload's bytes start at
    // Offset in the staging buffer, a buffer upload writes Width bytes at X, and a download lands in
    // Readback.
    private readonly record struct Transfer(TransferKind Kind, nint Target, int Offset, int X, int Y, int Width, int Height, bool Cycle, nint Readback = 0);
}
