using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Gpu;

// SDL_GPULoadOp, less the don't-care the host never uses.
internal enum GpuLoadOp
{
    Load,
    Clear,
}

// SDL_GPUColorTargetInfo for a target of one level and layer, stored at the pass's end.
internal readonly record struct GpuColorTarget(GpuTexture Texture, GpuLoadOp Load, Vector4 ClearColor = default);

// SDL_GPUTextureSamplerBinding, field for field: the handles a bind passes SDL, taken when it is made.
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct GpuTextureSamplerBinding
{
    private readonly nint _texture;
    private readonly nint _sampler;

    internal GpuTextureSamplerBinding(GpuTexture texture, GpuSampler sampler)
    {
        _texture = texture.Handle;
        _sampler = sampler.Handle;
    }
}

// SDL_GPUCommandBuffer: one frame's work, executed in order once submitted.
internal readonly unsafe struct GpuCommandBuffer(GpuDevice device, nint handle)
{
    internal GpuCopyPass BeginCopyPass() => new(device, Sdl3.SDL_BeginGPUCopyPass(handle));

    internal GpuRenderPass BeginRenderPass(in GpuColorTarget target)
    {
        Sdl3.GpuColorTargetInfo info = new()
        {
            Texture = target.Texture.Handle,
            ClearColor = new Sdl3.FColor { R = target.ClearColor.X, G = target.ClearColor.Y, B = target.ClearColor.Z, A = target.ClearColor.W },
            LoadOp = target.Load,
            StoreOp = Sdl3.GpuStoreOpStore,
        };

        return new GpuRenderPass(Sdl3.SDL_BeginGPURenderPass(handle, &info, 1, null));
    }

    // The bytes uniform buffer slot reads until the next push to it, for every later draw in this
    // command buffer.
    internal void PushVertexUniformData<T>(int slot, in T data)
        where T : unmanaged
    {
        fixed (T* bytes = &data)
        {
            Sdl3.SDL_PushGPUVertexUniformData(handle, (uint)slot, bytes, (uint)sizeof(T));
        }
    }

    internal void PushFragmentUniformData(int slot, ReadOnlySpan<byte> data)
    {
        fixed (byte* bytes = data)
        {
            Sdl3.SDL_PushGPUFragmentUniformData(handle, (uint)slot, bytes, (uint)data.Length);
        }
    }

    // The texture the window shows once this command buffer is submitted, at the swapchain's own extent,
    // or null when the window shows nothing new this frame. Outside every pass, and once a frame. Never
    // waits.
    internal GpuTexture? AcquireSwapchainTexture() => device.AcquireSwapchainTexture(handle);

    internal void Submit() => device.Submit(handle);

    // Submits, then blocks until the GPU has run every command, after which a readback holds its texels.
    internal void SubmitAndWait() => device.SubmitAndWait(handle);
}

// SDL_GPUCopyPass. Every upload and download a command buffer records sits in a copy pass outside
// its render passes.
internal readonly struct GpuCopyPass(GpuDevice device, nint handle)
{
    // Writes texels, rows of width tightly packed in the texture's format, to the region at x, y.
    internal void UploadToTexture(GpuTexture texture, int x, int y, int width, int height, ReadOnlySpan<byte> texels) =>
        device.UploadToTexture(handle, texture, x, y, width, height, texels);

    // Writes data at offset bytes into buffer. Cycling hands the write a fresh store when draws
    // already recorded still read the old one.
    internal void UploadToBuffer(GpuBuffer buffer, int offset, ReadOnlySpan<byte> data, bool cycle) =>
        device.UploadToBuffer(handle, buffer, offset, data, cycle);

    // Reads every texel of texture, which is the readback's extent and format.
    internal void DownloadFromTexture(GpuTexture texture, GpuReadback readback) => device.DownloadFromTexture(texture, readback);

    internal void End() => device.EndCopyPass(handle);
}

// SDL_GPURenderPass. Its viewport and scissor start at the whole target.
internal readonly unsafe struct GpuRenderPass(nint handle)
{
    internal void SetViewport(int x, int y, int width, int height)
    {
        Sdl3.GpuViewport viewport = new() { X = x, Y = y, W = width, H = height, MinDepth = 0f, MaxDepth = 1f };
        Sdl3.SDL_SetGPUViewport(handle, &viewport);
    }

    internal void SetScissor(int x, int y, int width, int height)
    {
        Sdl3.Rect scissor = new() { X = x, Y = y, W = width, H = height };
        Sdl3.SDL_SetGPUScissor(handle, &scissor);
    }

    internal void BindGraphicsPipeline(GpuPipeline pipeline) => Sdl3.SDL_BindGPUGraphicsPipeline(handle, pipeline.Handle);

    internal void BindVertexStorageBuffer(int slot, GpuBuffer buffer)
    {
        nint bound = buffer.Handle;
        Sdl3.SDL_BindGPUVertexStorageBuffers(handle, (uint)slot, &bound, 1);
    }

    internal void BindFragmentSamplers(int firstSlot, ReadOnlySpan<GpuTextureSamplerBinding> bindings)
    {
        fixed (GpuTextureSamplerBinding* bound = bindings)
        {
            Sdl3.SDL_BindGPUFragmentSamplers(handle, (uint)firstSlot, bound, (uint)bindings.Length);
        }
    }

    internal void DrawPrimitives(int vertexCount, int instanceCount, int firstVertex, int firstInstance) =>
        Sdl3.SDL_DrawGPUPrimitives(handle, (uint)vertexCount, (uint)instanceCount, (uint)firstVertex, (uint)firstInstance);

    internal void End() => Sdl3.SDL_EndGPURenderPass(handle);
}
