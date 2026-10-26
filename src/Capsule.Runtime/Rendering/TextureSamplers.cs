using Capsule.Rendering;
using Capsule.Runtime.Gpu;

namespace Capsule.Runtime.Rendering;

// The sampler for each sampling mode, clamped at the texture's edge. The one mapping from a mode to
// its sampler, which a texture's own sampling resolves through too.
internal sealed class TextureSamplers(GpuDevice device)
{
    private readonly GpuSampler _point = device.CreateSampler(new GpuSamplerInfo(GpuFilter.Nearest, GpuSamplerAddressMode.ClampToEdge));
    private readonly GpuSampler _linear = device.CreateSampler(new GpuSamplerInfo(GpuFilter.Linear, GpuSamplerAddressMode.ClampToEdge));

    internal GpuSampler Get(TextureSampling sampling) => sampling switch
    {
        TextureSampling.Linear => _linear,
        TextureSampling.Point => _point,
        _ => throw new ArgumentOutOfRangeException(nameof(sampling), sampling, "Unknown texture sampling mode."),
    };
}
