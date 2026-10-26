using System.Numerics;
using System.Runtime.InteropServices;

namespace Capsule.Runtime.Gpu;

// SDL_GPULoadOp.
internal enum GpuLoadOp
{
    Load,
    Clear,
}

// SDL_GPUColorTargetInfo for a target of one level and layer, stored at the pass's end.
internal readonly record struct GpuColorTarget(GpuTexture Texture, GpuLoadOp Load, Vector4 ClearColor = default);

// SDL_GPUTextureSamplerBinding.
internal readonly record struct GpuTextureSamplerBinding(GpuTexture Texture, GpuSampler Sampler);

// SDL_GPUCommandBuffer: one frame's or one upload's work, executed in order once submitted.
internal readonly struct GpuCommandBuffer(GpuDevice device)
{
    // The texture the window shows this frame, or null when the window has no area.
    internal GpuTexture? AcquireSwapchainTexture() => device.AcquireSwapchainTexture();

    internal GpuCopyPass BeginCopyPass() => new(device);

    internal GpuRenderPass BeginRenderPass(in GpuColorTarget target)
    {
        device.BeginRenderPass(in target);

        return new GpuRenderPass(device);
    }

    // The bytes uniform buffer slot reads until the next push to it, for every later draw in this
    // command buffer.
    internal void PushVertexUniformData<T>(int slot, in T data)
        where T : unmanaged =>
        device.PushUniformData(GpuShaderStage.Vertex, slot, MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in data)));

    internal void PushFragmentUniformData(int slot, ReadOnlySpan<byte> data) =>
        device.PushUniformData(GpuShaderStage.Fragment, slot, data);

    internal void Submit() => device.Submit();
}

// SDL_GPUCopyPass. Every upload and download a command buffer records sits in a copy pass outside
// its render passes.
internal readonly struct GpuCopyPass(GpuDevice device)
{
    // Writes texels, rows of width tightly packed in the texture's format, to the region at x, y.
    internal void UploadToTexture(GpuTexture texture, int x, int y, int width, int height, ReadOnlySpan<byte> texels) =>
        device.UploadToTexture(texture, x, y, width, height, texels);

    // Writes data at offset bytes into buffer. Cycling hands the write a fresh store when draws
    // already submitted still read the old one.
    internal void UploadToBuffer(GpuBuffer buffer, int offset, ReadOnlySpan<byte> data, bool cycle) =>
        device.UploadToBuffer(buffer, offset, data, cycle);

    internal void DownloadFromTexture(GpuTexture texture, GpuReadback readback) => device.DownloadFromTexture(texture, readback);

    internal void End()
    {
    }
}

// SDL_GPURenderPass. Its viewport and scissor start at the whole target.
internal readonly struct GpuRenderPass(GpuDevice device)
{
    internal void SetViewport(int x, int y, int width, int height) => device.SetViewport(x, y, width, height);

    internal void SetScissor(int x, int y, int width, int height) => device.SetScissor(x, y, width, height);

    internal void BindGraphicsPipeline(GpuPipeline pipeline) => device.BindGraphicsPipeline(pipeline);

    internal void BindVertexStorageBuffer(int slot, GpuBuffer buffer) => device.BindVertexStorageBuffer(slot, buffer);

    internal void BindFragmentSamplers(int firstSlot, ReadOnlySpan<GpuTextureSamplerBinding> bindings) =>
        device.BindFragmentSamplers(firstSlot, bindings);

    internal void DrawPrimitives(int vertexCount, int instanceCount, int firstVertex, int firstInstance) =>
        device.DrawPrimitives(vertexCount, instanceCount, firstVertex, firstInstance);

    internal void End() => device.EndRenderPass();
}
